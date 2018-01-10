﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Jobs.Validation.PackageSigning.Messages;
using NuGet.Jobs.Validation.PackageSigning.Storage;
using NuGet.Packaging.Signing;
using NuGet.Services.Validation;
using NuGet.Services.Validation.Issues;
using NuGetGallery;

namespace NuGet.Jobs.Validation.PackageSigning.ExtractAndValidateSignature
{
    public class SignatureValidator : ISignatureValidator
    {
        private readonly IPackageSigningStateService _packageSigningStateService;
        private readonly IPackageSignatureVerifier _packageSignatureVerifier;
        private readonly ISignaturePartsExtractor _signaturePartsExtractor;
        private readonly IEntityRepository<Certificate> _certificates;
        private readonly ILogger<SignatureValidator> _logger;

        public SignatureValidator(
            IPackageSigningStateService packageSigningStateService,
            IPackageSignatureVerifier packageSignatureVerifier,
            ISignaturePartsExtractor signaturePartsExtractor,
            IEntityRepository<Certificate> certificates,
            ILogger<SignatureValidator> logger)
        {
            _packageSigningStateService = packageSigningStateService ?? throw new ArgumentNullException(nameof(packageSigningStateService));
            _packageSignatureVerifier = packageSignatureVerifier ?? throw new ArgumentNullException(nameof(packageSignatureVerifier));
            _signaturePartsExtractor = signaturePartsExtractor ?? throw new ArgumentNullException(nameof(signaturePartsExtractor));
            _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SignatureValidatorResult> ValidateAsync(
            int packageKey,
            ISignedPackageReader signedPackageReader,
            SignatureValidationMessage message,
            CancellationToken cancellationToken)
        {
            if (!await signedPackageReader.IsSignedAsync(cancellationToken))
            {
                return await HandleUnsignedPackageAsync(packageKey, message);
            }
            else
            {
                return await HandleSignedPackageAsync(packageKey, signedPackageReader, message, cancellationToken);
            }
        }
        
        private async Task<SignatureValidatorResult> HandleUnsignedPackageAsync(int packageKey, SignatureValidationMessage message)
        {
            _logger.LogInformation(
                "Package {PackageId} {PackageVersion} is unsigned, no additional validations necessary for {ValidationId}.",
                message.PackageId,
                message.PackageVersion,
                message.ValidationId);

            return await AcceptAsync(packageKey, message, PackageSigningStatus.Unsigned);
        }

        private async Task<SignatureValidatorResult> HandleSignedPackageAsync(
            int packageKey,
            ISignedPackageReader signedPackageReader,
            SignatureValidationMessage message,
            CancellationToken cancellationToken)
        {
            // Block packages that don't have exactly one signature.
            var packageSignatures = await signedPackageReader.GetSignaturesAsync(cancellationToken);
            if (packageSignatures.Count != 1)
            {
                _logger.LogInformation(
                    "Signed package {PackageId} {PackageVersion} is blocked for validation {ValidationId} since it has {SignatureCount} signatures.",
                    message.PackageId,
                    message.PackageVersion,
                    message.ValidationId,
                    packageSignatures.Count);

                return await RejectAsync(packageKey, message);
            }

            // Block packages with any unknown signing certificates.
            var packageThumbprints = GetThumbprints(packageSignatures);
            var knownThumbprints = _certificates
                .GetAll()
                .Where(c => packageThumbprints.Contains(c.Thumbprint))
                .Select(c => c.Thumbprint)
                .ToList();
            
            var unknownThumbprints = packageThumbprints.Except(knownThumbprints);
            if (unknownThumbprints.Any())
            {
                _logger.LogInformation(
                    "Signed package {PackageId} {PackageVersion} is blocked for validation {ValidationId} since it has unknown certificate thumbprints: {UnknownThumbprints}",
                    message.PackageId,
                    message.PackageVersion,
                    message.ValidationId,
                    unknownThumbprints);

                return await RejectAsync(
                    packageKey,
                    message,
                    new PackageIsSigned());
            }

            // Call the "verify" API, which does the main logic of signature validation.
            var verifyResult = await _packageSignatureVerifier.VerifySignaturesAsync(
                signedPackageReader,
                cancellationToken);
            if (!verifyResult.Valid)
            {
                var errorIssues = verifyResult
                    .Results
                    .SelectMany(x => x.GetErrorIssues())
                    .ToList();

                var errorsForLogs = errorIssues
                    .Select(x => $"{x.Code}: {x.Message}")
                    .ToList();
                var warningsForLogs = verifyResult
                    .Results
                    .SelectMany(x => x.GetWarningIssues())
                    .Select(x => $"{x.Code}: {x.Message}")
                    .ToList();

                _logger.LogInformation(
                    "Signed package {PackageId} {PackageVersion} is blocked for validation {ValidationId} due to verify failures. Errors: {Errors} Warnings: {Warnings}",
                    message.PackageId,
                    message.PackageVersion,
                    message.ValidationId,
                    errorsForLogs,
                    warningsForLogs);

                return await RejectAsync(
                    packageKey,
                    message,
                    errorIssues
                        .Select(x => new ClientSigningVerificationFailure(x.Code.ToString(), x.Message))
                        .ToArray());
            }
            
            _logger.LogInformation(
                "Signed package {PackageId} {PackageVersion} for validation {ValidationId} is valid with certificate thumbprints: {PackageThumbprints}",
                message.PackageId,
                message.PackageVersion,
                message.ValidationId,
                packageThumbprints);

            // Extract all of the signature artifacts and persist them.
            await _signaturePartsExtractor.ExtractAsync(signedPackageReader, cancellationToken);

            // Mark this package as signed.
            return await AcceptAsync(packageKey, message, PackageSigningStatus.Valid);
        }

        private HashSet<string> GetThumbprints(IEnumerable<Signature> signatures)
        {
            return new HashSet<string>(signatures
                .Select(x => x.SignerInfo.Certificate.ComputeSHA256Thumbprint()));
        }

        private async Task<SignatureValidatorResult> RejectAsync(
            int packageKey,
            SignatureValidationMessage message,
            params IValidationIssue[] issues)
        {
            await _packageSigningStateService.SetPackageSigningState(
                packageKey,
                message.PackageId,
                message.PackageVersion,
                status: PackageSigningStatus.Invalid);

            return new SignatureValidatorResult(ValidationStatus.Failed, issues);
        }

        private async Task<SignatureValidatorResult> AcceptAsync(
            int packageKey,
            SignatureValidationMessage message,
            PackageSigningStatus status)
        {
            await _packageSigningStateService.SetPackageSigningState(
                packageKey,
                message.PackageId,
                message.PackageVersion,
                status);

            return new SignatureValidatorResult(ValidationStatus.Succeeded);
        }
    }
}
