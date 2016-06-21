﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace NuGet.Services.KeyVault
{
    public class KeyVaultConfiguration
    {
        public string VaultName { get; private set; }
        public string ClientId { get; private set; }
        public string CertificateThumbprint { get; private set; }
        public bool ValidateCertificate { get; private set; }

        public KeyVaultConfiguration(string vaultName, string clientId, string certificateThumbprint, bool validateCertificate)
        {
            if (string.IsNullOrWhiteSpace(vaultName))
            {
                throw new ArgumentNullException(nameof(vaultName));
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentNullException(nameof(clientId));
            }

            if (string.IsNullOrWhiteSpace(certificateThumbprint))
            {
                throw new ArgumentNullException(nameof(certificateThumbprint));
            }

            VaultName = vaultName;
            ClientId = clientId;
            CertificateThumbprint = certificateThumbprint;
            ValidateCertificate = validateCertificate;
        }
    }
}
