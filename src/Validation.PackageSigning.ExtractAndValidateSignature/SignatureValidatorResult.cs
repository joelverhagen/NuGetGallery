﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Services.Validation;

namespace NuGet.Jobs.Validation.PackageSigning.ExtractAndValidateSignature
{
    public class SignatureValidatorResult
    {
        public SignatureValidatorResult(ValidationStatus state) 
            : this(state, new IValidationIssue[0])
        {
        }

        public SignatureValidatorResult(ValidationStatus state, IReadOnlyList<IValidationIssue> issues)
        {
            State = state;
            Issues = issues ?? throw new ArgumentNullException(nameof(issues));

            if (state != ValidationStatus.Failed
                && state != ValidationStatus.Succeeded
                && issues.Any())
            {
                throw new ArgumentException("Issues are only allowed for terminal states.", nameof(issues));
            }
        }

        public ValidationStatus State { get; }
        public IReadOnlyList<IValidationIssue> Issues { get; }
    }
}