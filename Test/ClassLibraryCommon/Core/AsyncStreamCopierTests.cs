﻿// -----------------------------------------------------------------------------------------
// <copyright file="AsyncStreamCopierTests.cs" company="Microsoft">
//    Copyright 2013 Microsoft Corporation
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
// -----------------------------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Core.Executor;
using Microsoft.Azure.Storage.Core.Util;
using Microsoft.Azure.Storage.Shared.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Storage.Core
{
    [TestClass]
    public class AsyncStreamCopierTests : TestBase
    {
        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier and Count")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore)]
        [TestCategory(TenantTypeCategory.DevFabric)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyMaxLengthTest()
        {
            List<Task> tasks = new List<Task>();
            // Boundary tests
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, true, 10, -1, true, 10, -1, null, true),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, false, 10, -1, true, 10, -1, null, true),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, true, 10, -1, false, 10, -1, null, true),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, false, 10, -1, false, 10, -1, null, true),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, true, 10, -1, true, 10, -1, null, false),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, false, 10, -1, true, 10, -1, null, false),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, true, 10, -1, false, 10, -1, null, false),
                "Stream is longer than the allowed length.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<InvalidOperationException>(
                () => ValidateCopier((16 * 1024 * 1024), null, (16 * 1024 * 1024) - 1, false, 10, -1, false, 10, -1, null, false),
                "Stream is longer than the allowed length.")));

            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, true, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, false, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, true, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, false, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, true, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, false, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, true, 10, -1, false, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, 16 * 1024 * 1024, false, 10, -1, false, 10, -1, null, false)));

            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, true, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, false, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, true, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, false, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, true, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, false, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, true, 10, -1, false, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, null, (16 * 1024 * 1024) + 1, false, 10, -1, false, 10, -1, null, false)));

            Task.WaitAll(tasks.ToArray());
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier and Count")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore)]
        [TestCategory(TenantTypeCategory.DevFabric)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyCopyLengthTest()
        {
            List<Task> tasks = new List<Task>();
            // Copy half the stream
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, true, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, false, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, true, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, false, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, true, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, false, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, true, 10, -1, false, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 8 * 1024 * 1024, null, false, 10, -1, false, 10, -1, null, false)));

            // Boundary tests
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, true, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, false, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, true, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, false, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, true, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, false, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, true, 10, -1, false, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) - 1, null, false, 10, -1, false, 10, -1, null, false)));

            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, true, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, false, 10, -1, true, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, true, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, false, 10, -1, false, 10, -1, null, true)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, true, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, false, 10, -1, true, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, true, 10, -1, false, 10, -1, null, false)));
            tasks.Add(Task.Run(() => ValidateCopier(16 * 1024 * 1024, 16 * 1024 * 1024, null, false, 10, -1, false, 10, -1, null, false)));

            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, true, 10, -1, true, 10, -1, null, true),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, false, 10, -1, true, 10, -1, null, true),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, true, 10, -1, false, 10, -1, null, true),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, false, 10, -1, false, 10, -1, null, true),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, true, 10, -1, true, 10, -1, null, false),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, false, 10, -1, true, 10, -1, null, false),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, true, 10, -1, false, 10, -1, null, false),
                "The given stream does not contain the requested number of bytes from its given position.")));
            tasks.Add(Task.Run(() => TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                () => ValidateCopier(16 * 1024 * 1024, (16 * 1024 * 1024) + 1, null, false, 10, -1, false, 10, -1, null, false),
                "The given stream does not contain the requested number of bytes from its given position.")));
            Task.WaitAll(tasks.ToArray());
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncSameSpeedTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 10, -1, true, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncAsyncSameSpeedTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 10, -1, false, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncSyncSameSpeedTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 10, -1, true, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncSameSpeedTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 10, -1, false, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncSlowInputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 50, -1, true, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncAsyncSlowInputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 50, -1, false, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncSyncSlowInputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 50, -1, true, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncSlowInputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 50, -1, false, 10, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncSlowOutputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 10, -1, true, 50, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncAsyncSlowOutputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, true, 10, -1, false, 50, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncSyncSlowOutputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 10, -1, true, 50, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncSlowOutputTest()
        {
            ValidateCopier(16 * 1024 * 1024, null, null, false, 10, -1, false, 50, -1, null, true);
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncFailInputTest()
        {
            TestHelper.ExpectedException<IOException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, true, 10, 5, true, 10, -1, null, true),
                "Stream should have thrown an exception");
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncFailOutputTest()
        {
            TestHelper.ExpectedException<IOException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, true, 10, -1, true, 10, 5, null, true),
                "Stream should have thrown an exception");
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncFailInputTest()
        {
            TestHelper.ExpectedException<IOException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, false, 10, 5, false, 10, -1, null, true),
                "Stream should have thrown an exception");
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncFailOutputTest()
        {
            TestHelper.ExpectedException<IOException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, false, 10, -1, false, 10, 5, null, true),
                "Stream should have thrown an exception");
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopySyncSyncTimeoutTest()
        {
            StorageException e = TestHelper.ExpectedException<StorageException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, true, 2000, -1, true, 2000, -1, DateTime.Now.AddSeconds(5), true),
                "Stream should have thrown an exception");

            Assert.IsInstanceOfType(e.InnerException, typeof(TimeoutException));
        }

        [TestMethod]
        [Description("Copy a stream to another using AsyncStreamCopier")]
        [TestCategory(ComponentCategory.Core)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void StreamCopyAsyncAsyncTimeoutTest()
        {
            StorageException e = TestHelper.ExpectedException<StorageException>(
                () => ValidateCopier(16 * 1024 * 1024, null, null, false, 2000, -1, false, 2000, -1, DateTime.Now.AddSeconds(5), true),
                "Stream should have thrown an exception");

            Assert.IsInstanceOfType(e.InnerException, typeof(TimeoutException));
        }

        private static void ValidateCopier(int bufferLength, long? copyLength, long? maxLength, bool inputSync, int inputDelayInMs, int inputFailRequest, bool outputSync, int outputDelayInMs, int outputFailRequest, DateTime? copyTimeout, bool seekable)
        {
            byte[] buffer = GetRandomBuffer(bufferLength);

            // Finds ceiling of division operation
            int expectedCallCount =
                (int)
                (copyLength.HasValue && buffer.Length > copyLength
                     ? (-1L + copyLength + Constants.DefaultBufferSize) / Constants.DefaultBufferSize
                     : (-1L + buffer.Length + Constants.DefaultBufferSize) / Constants.DefaultBufferSize);

            int totalDelayInMs = (expectedCallCount + 1) * inputDelayInMs + expectedCallCount * outputDelayInMs;

            DataValidationStream input = new DataValidationStream(buffer, inputSync, inputDelayInMs, inputFailRequest, seekable);
            DataValidationStream output = new DataValidationStream(buffer, outputSync, outputDelayInMs, outputFailRequest, seekable);
            RESTCommand<NullType> cmdWithTimeout = new RESTCommand<NullType>(new StorageCredentials(), null, null) { OperationExpiryTime = copyTimeout };
            ExecutionState<NullType> state = new ExecutionState<NullType>(cmdWithTimeout, null, null);
            StreamDescriptor copyState = new StreamDescriptor();

            using (ManualResetEvent waitHandle = new ManualResetEvent(false))
            {
                MockBufferManager mockBufferManager = new MockBufferManager(Constants.DefaultBufferSize);
                input.WriteToAsync(output, mockBufferManager, copyLength, maxLength, ChecksumRequested.None, state, copyState, CancellationToken.None, _ => waitHandle.Set());
                Assert.IsTrue(waitHandle.WaitOne(totalDelayInMs + 10 * 1000));
                Assert.AreEqual(0, mockBufferManager.OutstandingBufferCount, "Outstanding buffers not returned to IBufferManager");
            }

            if (inputFailRequest >= 0)
            {
                Assert.AreEqual(input.LastException, state.ExceptionRef);
                Assert.AreEqual(inputFailRequest, input.ReadCallCount);
            }

            if (outputFailRequest >= 0)
            {
                Assert.AreEqual(output.LastException, state.ExceptionRef);
                Assert.AreEqual(outputFailRequest, output.WriteCallCount);
            }

            if (state.ExceptionRef != null)
            {
                throw state.ExceptionRef;
            }

            Assert.AreEqual(copyLength.HasValue ? copyLength : buffer.Length, copyState.Length);
            Assert.AreEqual(copyLength.HasValue ? expectedCallCount : expectedCallCount + 1, input.ReadCallCount);
            Assert.AreEqual(expectedCallCount, output.WriteCallCount);
        }
    }
}
