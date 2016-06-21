﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NuGet.Services.KeyVault;
using Xunit;

namespace NuGet.Services.KeyVaultUnitTests
{
    public class KeyVaultReaderFormatterTests
    {
        public static IEnumerable<object[]> _testFormatParameters = new List<object[]>
        {
            new object[] // DB connection string
            {
                @"Server=tcp:myserver.database.windows.net,1433;Database=myDB;User ID=$$secret1$$;Password=$$secret2$$;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;",
                @"Server=tcp:myserver.database.windows.net,1433;Database=myDB;User ID=SECRET1;Password=SECRET2;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
            },
            new object[] // Empty string
            {
                "",
                ""
            },
            new object[] // String without secrets
            {
                "the red fox jumped over the lazy dog",
                "the red fox jumped over the lazy dog"
            },
            new object[] // Only secrets
            {
                "$$secret1$$$$secret2$$",
                "SECRET1SECRET2"
            },
            new object[] // Invalid input
            {
                "abc$$abc",
                "abc$$abc"
            },
            new object[] // Invalid input
            {
                "$$$$$",
                "$$$$$"
            }
        };

        [Theory]
        [MemberData("_testFormatParameters")]
        public void TestFormat(string input, string expectedOutput)
        {
            // Arrange
            var mockKeyVault = new Mock<ISecretReader>();
            mockKeyVault.Setup(x => x.ReadSecretAsync(It.IsAny<string>())).Returns((string s) => Task.FromResult(s.ToUpper()));

            // Act
            string formattedString = mockKeyVault.Object.Format(input);

            // Assert
            formattedString.ShouldBeEquivalentTo(expectedOutput);
        }
    }
}
