﻿// -----------------------------------------------------------------------------------------
// <copyright file="CloudBlockBlobTest.cs" company="Microsoft">
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
using Microsoft.Azure.Storage.Blob.Protocol;
using Microsoft.Azure.Storage.Core.Util;
using Microsoft.Azure.Storage.Shared.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Storage.Blob
{
    [TestClass]
    public class CloudBlockBlobTest : BlobTestBase
    {
        private static void CreateForTest(CloudBlockBlob blob, int blockCount, int blockSize, bool isAsync, bool commit = true)
        {
            byte[] buffer = GetRandomBuffer(blockSize);
            List<string> blocks = GetBlockIdList(blockCount);

            using (AutoResetEvent waitHandle = new AutoResetEvent(false))
            {
                foreach (string block in blocks)
                {
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        if (isAsync)
                        {
                            IAsyncResult result = blob.BeginPutBlock(block, stream, null,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndPutBlock(result);
                        }
                        else
                        {
                            blob.PutBlock(block, stream, null);
                        }
                    }
                }

                if (commit)
                {
                    if (isAsync)
                    {
                        IAsyncResult result = blob.BeginPutBlockList(blocks,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndPutBlockList(result);
                    }
                    else
                    {
                        blob.PutBlockList(blocks);
                    }
                }
            }
        }

        //
        // Use TestInitialize to run code before running each test 
        [TestInitialize()]
        public void MyTestInitialize()
        {
            if (TestBase.BlobBufferManager != null)
            {
                TestBase.BlobBufferManager.OutstandingBufferCount = 0;
            }
        }
        //
        // Use TestCleanup to run code after each test has run
        [TestCleanup()]
        public void MyTestCleanup()
        {
            if (TestBase.BlobBufferManager != null)
            {
                Assert.AreEqual(0, TestBase.BlobBufferManager.OutstandingBufferCount);
            }
        }

#if TASK
        private static void CreateForTestTask(CloudBlockBlob blob, int blockCount, int blockSize, bool commit = true)
        {
            byte[] buffer = GetRandomBuffer(blockSize);
            List<string> blocks = GetBlockIdList(blockCount);

            foreach (string block in blocks)
            {
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.PutBlockAsync(block, stream, null).Wait();
                }
            }

            if (commit)
            {
                blob.PutBlockListAsync(blocks, null, null, null).Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Test blob name validation.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlobNameValidation()
        {
            NameValidator.ValidateBlobName("alpha");
            NameValidator.ValidateBlobName("4lphanum3r1c");
            NameValidator.ValidateBlobName("CAPSLOCK");
            NameValidator.ValidateBlobName("white space");
            NameValidator.ValidateBlobName("ºth3r(h@racter$");
            NameValidator.ValidateBlobName(string.Join("/", Enumerable.Repeat("a", 254)));

            TestInvalidBlobHelper(string.Empty, "No empty strings.", "Invalid blob name. The blob name may not be null, empty, or whitespace only.");
            TestInvalidBlobHelper(null, "No null strings.", "Invalid blob name. The blob name may not be null, empty, or whitespace only.");
            TestInvalidBlobHelper(new string('n', 1025), "Maximum 1024 characters.", "Invalid blob name length. The blob name must be between 1 and 1024 characters long.");
            TestInvalidBlobHelper(string.Join("/", Enumerable.Repeat("a", 255)), "Maximum 254 segments.", "The count of URL path segments (strings between '/' characters) as part of the blob name cannot exceed 254.");
        }

        private void TestInvalidBlobHelper(string blobName, string failMessage, string exceptionMessage)
        {
            try
            {
                NameValidator.ValidateBlobName(blobName);
                Assert.Fail(failMessage);
            }
            catch (ArgumentException e)
            {
                Assert.AreEqual(exceptionMessage, e.Message);
            }
        }

        [TestMethod]
        [Description("Get a block blob reference using its constructor")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobConstructor()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
            CloudBlockBlob blob2 = new CloudBlockBlob(blob.StorageUri, null, credentials:null);
            Assert.AreEqual(blob.Name, blob2.Name);
            Assert.AreEqual(blob.StorageUri, blob2.StorageUri);
            Assert.AreEqual(blob.Container.StorageUri, blob2.Container.StorageUri);
            Assert.AreEqual(blob.ServiceClient.StorageUri, blob2.ServiceClient.StorageUri);
        }

        [TestMethod]
        [Description("Create a zero-length block blob and then delete it")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCreateAndDelete()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 0, 0, false);
                Assert.IsTrue(blob.Exists());

                Assert.IsTrue(blob.Properties.Created.HasValue);
                Assert.IsTrue(blob.Properties.Created.Value > DateTime.Now.AddMinutes(-1));

                blob.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Test blob set tier batch")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        public async Task CloudBlockBlobSetTierBatchTestAsync()
        {
            // TODO: Test arg validation. Test uber request failure. Sub request failure. Varying numbers of operations (0, 1, many). Failure then success. Success then failure. Mixed failures and successes. Multiple of each.
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                var blobName1 = "ºth3r(h@racter$/?:.&%=";
                var blobName2 = "white space";
                var blobNameUncreated = "uncreated";

                var blob1 = container.GetBlockBlobReference(blobName1);
                var blob2 = container.GetBlockBlobReference(blobName2);
                var blob3 = container.GetBlockBlobReference(blobNameUncreated);

                CreateForTest(blob1, 0, 0, false);

                // Test a batch with one successful operation.
                BlobSetTierBatchOperation batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob1, StandardBlobTier.Cool, null, null);

                IList<BlobBatchSubOperationResponse> results = await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);

                Assert.AreEqual(results.Count, 1);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.OK);

                // Test a batch with multiple successful operations.
                // TODO: Test with page blob when it is possible to get a premium account with batch enabled.
                CreateForTest(blob2, 0, 0, false);

                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob1, StandardBlobTier.Hot);
                batch.AddSubOperation(blob2, StandardBlobTier.Cool);

                results = await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);

                Assert.AreEqual(results.Count, 2);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.OK);

                // Test a batch with one failure.

                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob3, StandardBlobTier.Hot);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(e.SuccessfulResponses.Count, 0);
                    Assert.AreEqual(e.ErrorResponses.Count, 1);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test a batch with multiple operations that all failed.
                batch.AddSubOperation(blob3, StandardBlobTier.Cool);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(e.SuccessfulResponses.Count, 0);
                    Assert.AreEqual(e.ErrorResponses.Count, 2);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test a batch with successful and failed operations interleaved. This tests parsing an error after a success and a success after an error.
                // Parsing an error after an error and a success after a success was covered above. This should cover all cases.
                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob1, StandardBlobTier.Hot);
                batch.AddSubOperation(blob3, StandardBlobTier.Cool);
                batch.AddSubOperation(blob2, StandardBlobTier.Archive);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(e.SuccessfulResponses.Count, 2);
                    Assert.AreEqual(e.ErrorResponses.Count, 1);
                    ValidateSuccessfulBatchResponse(e.SuccessfulResponses, HttpStatusCode.OK);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test an empty batch. This also tests a failed uber request. 
                batch = new BlobSetTierBatchOperation();

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (StorageException)
                {
                    
                }

                // SAS auth sub request tests
                var blob4Name = "blob4";
                var blob5Name = "blob5";
                var blob4 = container.GetBlockBlobReference(blob4Name);
                var blob5 = container.GetBlockBlobReference(blob5Name);
                CreateForTest(blob4, 1, 1, false);
                CreateForTest(blob5, 1, 1, false);

                var policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddDays(1),
                    Permissions = SharedAccessBlobPermissions.Add | SharedAccessBlobPermissions.Create | SharedAccessBlobPermissions.Write
                };

                string sas = container.GetSharedAccessSignature(policy);
                var sasContainer = new CloudBlobContainer(new Uri(container.Uri.ToString() + sas));
                blob4 = sasContainer.GetBlockBlobReference(blob4Name);
                blob5 = sasContainer.GetBlockBlobReference(blob5Name);

                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob4, StandardBlobTier.Hot);
                batch.AddSubOperation(blob5, StandardBlobTier.Cool);

                await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Test blob delete batch")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        public async Task CloudBlockBlobDeleteBatchTestAsync()
        {
            // TODO: Test arg validation. Test uber request failure. Sub request failure. Varying numbers of operations (0, 1, many). Failure then success. Success then failure. Mixed failures and successes. Multiple of each.
            var container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                var blobName1 = "ºth3r(h@racter$/?:.&%=";
                var blobName2 = "white space";
                var blobNameUncreated = "uncreated";

                var blob1 = container.GetBlockBlobReference(blobName1);
                var blob2 = container.GetBlockBlobReference(blobName2);
                var blob3 = container.GetBlockBlobReference(blobNameUncreated);

                CreateForTest(blob1, 1, 1, false);
                CreateForTest(blob2, 1, 1, false);

                var batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob1);
                batch.AddSubOperation(blob2);

                var results = await blob1.ServiceClient.ExecuteBatchAsync(batch);

                Assert.AreEqual(2, results.Count);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.Accepted);

                // Test a batch with one failure.
                CreateForTest(blob1, 1, 1, false);
                CreateForTest(blob2, 1, 1, false);

                batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob1);
                batch.AddSubOperation(blob2);
                batch.AddSubOperation(blob3);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(2, e.SuccessfulResponses.Count);
                    Assert.AreEqual(1, e.ErrorResponses.Count);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test a batch with multiple operations that all failed.
                batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob1);
                batch.AddSubOperation(blob2);
                batch.AddSubOperation(blob3);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(0, e.SuccessfulResponses.Count);
                    Assert.AreEqual(3, e.ErrorResponses.Count);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test a batch with successful and failed operations interleaved. This tests parsing an error after a success and a success after an error.
                // Parsing an error after an error and a success after a success was covered above. This should cover all cases.
                CreateForTest(blob1, 1, 1, false);
                CreateForTest(blob2, 1, 1, false);

                batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob1);
                batch.AddSubOperation(blob3);
                batch.AddSubOperation(blob2);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (BlobBatchException e)
                {
                    Assert.AreEqual(2, e.SuccessfulResponses.Count);
                    Assert.AreEqual(1, e.ErrorResponses.Count);
                    ValidateSuccessfulBatchResponse(e.SuccessfulResponses, HttpStatusCode.Accepted);
                    ValidateFailedBatchResponse(e.ErrorResponses);
                }

                // Test an empty batch. This also tests a failed uber request. 
                batch = new BlobDeleteBatchOperation();

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
                    Assert.Fail();
                }
                catch (StorageException)
                {

                }

                // SAS auth sub request tests
                CreateForTest(blob1, 1, 1, false);
                CreateForTest(blob2, 1, 1, false);

                var policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddDays(1),
                    Permissions = SharedAccessBlobPermissions.Delete
                };

                string sas = container.GetSharedAccessSignature(policy);
                var sasContainer = new CloudBlobContainer(new Uri(container.Uri.ToString() + sas));
                blob3 = sasContainer.GetBlockBlobReference(blobName1);
                var blob4 = sasContainer.GetBlockBlobReference(blobName2);

                batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob3);
                batch.AddSubOperation(blob4);

                await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);

            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Test blob delete batch with token auth")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        public async Task CloudBlockBlobDeleteBatchTokenAuthTestAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference(oauthTenant: true);
            try
            {
                // Arrange
                string blobName = "blobName";
                CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
                await container.CreateAsync();
                CreateForTest(blob, 1, 1, true);

                CloudBlobClient tokenClient = GetOAuthClient();
                CloudBlobContainer tokenContainer = tokenClient.GetContainerReference(container.Name);
                CloudBlockBlob tokenBlob = tokenContainer.GetBlockBlobReference(blobName);

                BlobDeleteBatchOperation batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(tokenBlob);

                // Act
                await blob.ServiceClient.ExecuteBatchAsync(batch);
                //await tokenBlob.DeleteAsync();
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Test blob set tier batch with token auth")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        public async Task CloudBlockBlobSetTierBatchTokenAuthTestAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference(oauthTenant: true);
            try
            {
                // Arrange
                string blobName = "blobName";
                CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
                await container.CreateAsync();
                CreateForTest(blob, 1, 1, true);

                CloudBlobClient tokenClient = GetOAuthClient();
                CloudBlobContainer tokenContainer = tokenClient.GetContainerReference(container.Name);
                CloudBlockBlob tokenBlob = tokenContainer.GetBlockBlobReference(blobName);

                BlobSetTierBatchOperation batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(tokenBlob, StandardBlobTier.Hot);

                // Act
                await blob.ServiceClient.ExecuteBatchAsync(batch);
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        private void ValidateSuccessfulBatchResponse(IList<BlobBatchSubOperationResponse> results, HttpStatusCode expectedSuccessCode)
        {
            int count = -1;
            foreach (BlobBatchSubOperationResponse result in results)
            {
                // The index sequence may not be continuous if errors and successes are mixed, but it should monotonically increase.
                Assert.IsTrue(result.OperationIndex > count);
                count = result.OperationIndex;

                Assert.AreEqual(expectedSuccessCode, result.StatusCode);
                Assert.IsTrue(result.Headers.ContainsKey(Constants.HeaderConstants.RequestIdHeader));
            }
        }

        private void ValidateFailedBatchResponse(IList<BlobBatchSubOperationError> errors)
        {
            int count = -1;
            foreach (BlobBatchSubOperationError error in errors)
            {
                // The index sequence may not be continuous if errors and successes are mixed, but it should monotonically increase.
                Assert.IsTrue(error.OperationIndex > count);
                count = error.OperationIndex;

                Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
                Assert.AreEqual(BlobErrorCodeStrings.BlobNotFound, error.ErrorCode);
                Assert.IsNotNull(error.ExtendedErrorInformation);
                Assert.IsNotNull(error.ExtendedErrorInformation.ErrorMessage);
            }
        }

        [TestMethod]
        [Description("Create a zero-length block blob and then delete it")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCreateAndDeleteAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 0, 0, false);
                IAsyncResult result;
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    result = blob.BeginExists(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    Assert.IsTrue(blob.EndExists(result));
                    result = blob.BeginDelete(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    blob.EndDelete(result);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Create a zero-length block blob and then delete it")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCreateAndDeleteTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.PutBlockListAsync(new List<string>()).Wait();
                Assert.IsTrue(blob.ExistsAsync().Result);
                blob.DeleteAsync().Wait();
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

#endif

        [TestMethod]
        [Description("Try to delete a non-existing block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExists()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                OperationContext op = new OperationContext
                {
                    CustomUserAgent = "did"
                };

                container.Create(BlobContainerPublicAccessType.Off, null, op);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                Assert.IsFalse(blob.DeleteIfExists());
                CreateForTest(blob, 0, 0, false);
                Assert.IsTrue(blob.DeleteIfExists());
                Assert.IsFalse(blob.DeleteIfExists());
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Try to delete a non-existing block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExistsAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    IAsyncResult result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob.EndDeleteIfExists(result));
                    CreateForTest(blob, 0, 0, true);
                    result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsTrue(blob.EndDeleteIfExists(result));
                    result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob.EndDeleteIfExists(result));
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Try to delete a non-existing block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExistsTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                Assert.IsFalse(blob.DeleteIfExistsAsync().Result);
                blob.PutBlockListAsync(new List<string>()).Wait();
                Assert.IsTrue(blob.DeleteIfExistsAsync().Result);
                Assert.IsFalse(blob.DeleteIfExistsAsync().Result);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Try to delete a non-existing block blob with write-only Account SAS permissions - SYNC")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExistsWithWriteOnlyPermissionsSync()
        {
            CloudBlobContainer container = GenerateRandomWriteOnlyBlobContainer();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                Assert.IsFalse(blob.DeleteIfExists());
                CreateForTest(blob, 0, 0, false);
                Assert.IsTrue(blob.DeleteIfExists());
                Assert.IsFalse(blob.DeleteIfExists());
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Try to delete a non-existing block blob with write-only Account SAS permissions - APM")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExistsWithWriteOnlyPermissionsAPM()
        {
            CloudBlobContainer container = GenerateRandomWriteOnlyBlobContainer();
            try
            {
                container.Create();

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    IAsyncResult result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob.EndDeleteIfExists(result));
                    CreateForTest(blob, 0, 0, true);
                    result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsTrue(blob.EndDeleteIfExists(result));
                    result = blob.BeginDeleteIfExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob.EndDeleteIfExists(result));
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Try to delete a non-existing block blob with write-only Account SAS permissions - TASK")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDeleteIfExistsWithWriteOnlyPermissionsTask()
        {
            CloudBlobContainer container = GenerateRandomWriteOnlyBlobContainer();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                Assert.IsFalse(blob.DeleteIfExistsAsync().Result);
                blob.PutBlockListAsync(new List<string>()).Wait();
                Assert.IsTrue(blob.DeleteIfExistsAsync().Result);
                Assert.IsFalse(blob.DeleteIfExistsAsync().Result);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Check a blob's existence")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobExists()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();

            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");

                Assert.IsFalse(blob2.Exists());

                CreateForTest(blob, 2, 1024, false);

                Assert.IsTrue(blob2.Exists());
                Assert.AreEqual(2048, blob2.Properties.Length);

                blob.Delete();

                Assert.IsFalse(blob2.Exists());
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Check a blob's existence")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobExistsAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();

            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result = blob2.BeginExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob2.EndExists(result));

                    CreateForTest(blob, 2, 1024, false);

                    result = blob2.BeginExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsTrue(blob2.EndExists(result));
                    Assert.AreEqual(2048, blob2.Properties.Length);

                    blob.Delete();

                    result = blob2.BeginExists(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Assert.IsFalse(blob2.EndExists(result));
                }
            }
            finally
            {
                container.Delete();
            }
        }

#if TASK
        [TestMethod]
        [Description("Check a blob's existence")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobExistsTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.CreateAsync().Wait();

            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");

                Assert.IsFalse(blob2.ExistsAsync().Result);

                CreateForTestTask(blob, 2, 1024);

                Assert.IsTrue(blob2.ExistsAsync().Result);
                Assert.AreEqual(2048, blob2.Properties.Length);

                blob.DeleteAsync().Wait();

                Assert.IsFalse(blob2.ExistsAsync().Result);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Verify the attributes of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFetchAttributes()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);
                Assert.AreEqual(-1, blob.Properties.Length);
                Assert.IsNotNull(blob.Properties.ETag);
                Assert.IsTrue(blob.Properties.LastModified > DateTimeOffset.UtcNow.AddMinutes(-5));
                Assert.IsNull(blob.Properties.CacheControl);
                Assert.IsNull(blob.Properties.ContentDisposition);
                Assert.IsNull(blob.Properties.ContentEncoding);
                Assert.IsNull(blob.Properties.ContentLanguage);
                Assert.IsNull(blob.Properties.ContentType);
                Assert.IsNull(blob.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unspecified, blob.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob.Properties.BlobType);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual(1024, blob2.Properties.Length);
                Assert.AreEqual(blob.Properties.ETag, blob2.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, blob2.Properties.LastModified);
                Assert.IsNull(blob2.Properties.CacheControl);
                Assert.IsNull(blob2.Properties.ContentDisposition);
                Assert.IsNull(blob2.Properties.ContentEncoding);
                Assert.IsNull(blob2.Properties.ContentLanguage);
                Assert.AreEqual("application/octet-stream", blob2.Properties.ContentType);
                Assert.IsNull(blob2.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unlocked, blob2.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob2.Properties.BlobType);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify the attributes of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFetchAttributesAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, true);
                Assert.AreEqual(-1, blob.Properties.Length);
                Assert.IsNotNull(blob.Properties.ETag);
                Assert.IsTrue(blob.Properties.LastModified > DateTimeOffset.UtcNow.AddMinutes(-5));
                Assert.IsNull(blob.Properties.CacheControl);
                Assert.IsNull(blob.Properties.ContentEncoding);
                Assert.IsNull(blob.Properties.ContentDisposition);
                Assert.IsNull(blob.Properties.ContentLanguage);
                Assert.IsNull(blob.Properties.ContentType);
                Assert.IsNull(blob.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unspecified, blob.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob.Properties.BlobType);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                using (ManualResetEvent waitHandle = new ManualResetEvent(false))
                {
                    IAsyncResult result = blob2.BeginFetchAttributes(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob2.EndFetchAttributes(result);
                }
                Assert.AreEqual(1024, blob2.Properties.Length);
                Assert.AreEqual(blob.Properties.ETag, blob2.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, blob2.Properties.LastModified);
                Assert.IsNull(blob2.Properties.CacheControl);
                Assert.IsNull(blob2.Properties.ContentDisposition);
                Assert.IsNull(blob2.Properties.ContentEncoding);
                Assert.IsNull(blob2.Properties.ContentLanguage);
                Assert.AreEqual("application/octet-stream", blob2.Properties.ContentType);
                Assert.IsNull(blob2.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unlocked, blob2.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob2.Properties.BlobType);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Verify the attributes of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFetchAttributesTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTestTask(blob, 1, 1024);
                Assert.AreEqual(-1, blob.Properties.Length);
                Assert.IsNotNull(blob.Properties.ETag);
                Assert.IsTrue(blob.Properties.LastModified > DateTimeOffset.UtcNow.AddMinutes(-5));
                Assert.IsNull(blob.Properties.CacheControl);
                Assert.IsNull(blob.Properties.ContentEncoding);
                Assert.IsNull(blob.Properties.ContentLanguage);
                Assert.IsNull(blob.Properties.ContentType);
                Assert.IsNull(blob.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unspecified, blob.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob.Properties.BlobType);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual(1024, blob2.Properties.Length);
                Assert.AreEqual(blob.Properties.ETag, blob2.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, blob2.Properties.LastModified);
                Assert.IsNull(blob2.Properties.CacheControl);
                Assert.IsNull(blob2.Properties.ContentEncoding);
                Assert.IsNull(blob2.Properties.ContentLanguage);
                Assert.AreEqual("application/octet-stream", blob2.Properties.ContentType);
                Assert.IsNull(blob2.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unlocked, blob2.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob2.Properties.BlobType);

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob3.Properties.ContentMD5);
                byte[] target = new byte[4];
                BlobRequestOptions options2 = new BlobRequestOptions();
                options2.UseTransactionalMD5 = true;
                blob3.Properties.ContentMD5 = "MDAwMDAwMDA=";
                blob3.DownloadRangeToByteArray(target, 0, 0, 4, options: options2);
                Assert.IsNull(blob3.Properties.ContentMD5);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif
        [TestMethod]
        [Description("Verify additional user-defined query parameters do not disrupt a normal request")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFetchAttributesSpecialQueryParameters()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                // Ensure that unkown query parameters set by the user are signed properly but ignored, allowing the operation to succeed
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);
                UriBuilder blobURIBuilder = new UriBuilder(blob.Uri);
                blobURIBuilder.Query = "MyQuery=value&YOURQUERY=value2";
                blob = new CloudBlockBlob(blobURIBuilder.Uri, blob.ServiceClient.Credentials);

                blob.FetchAttributes();
                Assert.AreEqual(1024, blob.Properties.Length);
                Assert.IsNotNull(blob.Properties.ETag);
                Assert.IsTrue(blob.Properties.LastModified > DateTimeOffset.UtcNow.AddMinutes(-5));
                Assert.IsNull(blob.Properties.CacheControl);
                Assert.IsNull(blob.Properties.ContentDisposition);
                Assert.IsNull(blob.Properties.ContentEncoding);
                Assert.IsNull(blob.Properties.ContentLanguage);
                Assert.AreEqual("application/octet-stream", blob.Properties.ContentType);
                Assert.IsNull(blob.Properties.ContentMD5);
                Assert.AreEqual(LeaseStatus.Unlocked, blob.Properties.LeaseStatus);
                Assert.AreEqual(BlobType.BlockBlob, blob.Properties.BlobType);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify setting the properties of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetProperties()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);
                string eTag = blob.Properties.ETag;
                DateTimeOffset lastModified = blob.Properties.LastModified.Value;

                Thread.Sleep(1000);

                blob.Properties.CacheControl = "no-transform";
                blob.Properties.ContentDisposition = "attachment";
                blob.Properties.ContentEncoding = "gzip";
                blob.Properties.ContentLanguage = "tr,en";
                blob.Properties.ContentMD5 = "MDAwMDAwMDA=";
                blob.Properties.ContentType = "text/html";
                blob.SetProperties();
                Assert.IsTrue(blob.Properties.LastModified > lastModified);
                Assert.AreNotEqual(eTag, blob.Properties.ETag);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual("no-transform", blob2.Properties.CacheControl);
                Assert.AreEqual("attachment", blob2.Properties.ContentDisposition);
                Assert.AreEqual("gzip", blob2.Properties.ContentEncoding);
                Assert.AreEqual("tr,en", blob2.Properties.ContentLanguage);
                Assert.AreEqual("MDAwMDAwMDA=", blob2.Properties.ContentMD5);
                Assert.AreEqual("text/html", blob2.Properties.ContentType);

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob1");
                using (MemoryStream stream = new MemoryStream())
                {
                    BlobRequestOptions options = new BlobRequestOptions()
                    {
                        DisableContentMD5Validation = true,
                    };
                    blob3.DownloadToStream(stream, null, options);
                }
                AssertAreEqual(blob2.Properties, blob3.Properties);

                CloudBlockBlob blob4 = (CloudBlockBlob)container.ListBlobs().First();
                AssertAreEqual(blob2.Properties, blob4.Properties);

                CloudBlockBlob blob5 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob5.Properties.ContentMD5);
                byte[] target = new byte[4];
                blob5.DownloadRangeToByteArray(target, 0, 0, 4);
                Assert.AreEqual("MDAwMDAwMDA=", blob5.Properties.ContentMD5);

                CloudBlockBlob blob6 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob6.Properties.ContentMD5);
                target = new byte[4];
                BlobRequestOptions options2 = new BlobRequestOptions();
                options2.UseTransactionalMD5 = true;
                blob6.DownloadRangeToByteArray(target, 0, 0, 4, options: options2);
                Assert.AreEqual("MDAwMDAwMDA=", blob6.Properties.ContentMD5);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify setting the properties of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetPropertiesAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    CreateForTest(blob, 1, 1024, true);
                    string eTag = blob.Properties.ETag;
                    DateTimeOffset lastModified = blob.Properties.LastModified.Value;

                    Thread.Sleep(1000);

                    blob.Properties.CacheControl = "no-transform";
                    blob.Properties.ContentDisposition = "attachment";
                    blob.Properties.ContentEncoding = "gzip";
                    blob.Properties.ContentLanguage = "tr,en";
                    blob.Properties.ContentMD5 = "MDAwMDAwMDA=";
                    blob.Properties.ContentType = "text/html";
                    IAsyncResult result = blob.BeginSetProperties(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob.EndSetProperties(result);
                    Assert.IsTrue(blob.Properties.LastModified > lastModified);
                    Assert.AreNotEqual(eTag, blob.Properties.ETag);

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    result = blob2.BeginFetchAttributes(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob2.EndFetchAttributes(result);
                    Assert.AreEqual("no-transform", blob2.Properties.CacheControl);
                    Assert.AreEqual("attachment", blob2.Properties.ContentDisposition);
                    Assert.AreEqual("gzip", blob2.Properties.ContentEncoding);
                    Assert.AreEqual("tr,en", blob2.Properties.ContentLanguage);
                    Assert.AreEqual("MDAwMDAwMDA=", blob2.Properties.ContentMD5);
                    Assert.AreEqual("text/html", blob2.Properties.ContentType);

                    CloudBlockBlob blob3 = container.GetBlockBlobReference("blob1");
                    using (MemoryStream stream = new MemoryStream())
                    {
                        BlobRequestOptions options = new BlobRequestOptions()
                        {
                            DisableContentMD5Validation = true,
                        };
                        result = blob3.BeginDownloadToStream(stream, null, options, null,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob3.EndDownloadToStream(result);
                    }
                    AssertAreEqual(blob2.Properties, blob3.Properties);

                    result = container.BeginListBlobsSegmented(null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    BlobResultSegment results = container.EndListBlobsSegmented(result);
                    CloudBlockBlob blob4 = (CloudBlockBlob)results.Results.First();
                    AssertAreEqual(blob2.Properties, blob4.Properties);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Verify setting the properties of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetPropertiesTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTestTask(blob, 1, 1024);
                string eTag = blob.Properties.ETag;
                DateTimeOffset lastModified = blob.Properties.LastModified.Value;

                Thread.Sleep(1000);

                blob.Properties.CacheControl = "no-transform";
                blob.Properties.ContentDisposition = "attachment";
                blob.Properties.ContentEncoding = "gzip";
                blob.Properties.ContentLanguage = "tr,en";
                blob.Properties.ContentMD5 = "MDAwMDAwMDA=";
                blob.Properties.ContentType = "text/html";
                blob.SetPropertiesAsync().Wait();
                Assert.IsTrue(blob.Properties.LastModified > lastModified);
                Assert.AreNotEqual(eTag, blob.Properties.ETag);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual("no-transform", blob2.Properties.CacheControl);
                Assert.AreEqual("attachment", blob2.Properties.ContentDisposition);
                Assert.AreEqual("gzip", blob2.Properties.ContentEncoding);
                Assert.AreEqual("tr,en", blob2.Properties.ContentLanguage);
                Assert.AreEqual("MDAwMDAwMDA=", blob2.Properties.ContentMD5);
                Assert.AreEqual("text/html", blob2.Properties.ContentType);

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob1");
                using (MemoryStream stream = new MemoryStream())
                {
                    BlobRequestOptions options = new BlobRequestOptions()
                    {
                        DisableContentMD5Validation = true,
                    };
                    blob3.DownloadToStreamAsync(stream, null, options, null).Wait();
                }
                AssertAreEqual(blob2.Properties, blob3.Properties);

                CloudBlockBlob blob4 = (CloudBlockBlob)container.ListBlobsSegmentedAsync(null).Result.Results.First();
                AssertAreEqual(blob2.Properties, blob4.Properties);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Try retrieving properties of a block blob using a page blob reference")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFetchAttributesInvalidType()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);

                CloudPageBlob blob2 = container.GetPageBlobReference("blob1");
                StorageException e = TestHelper.ExpectedException<StorageException>(
                    () => blob2.FetchAttributes(),
                    "Fetching attributes of a block blob using a page blob reference should fail");
                Assert.IsInstanceOfType(e.InnerException, typeof(InvalidOperationException));
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify that creating a block blob can also set its metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCreateWithMetadata()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                string md5 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });

                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.Metadata["key1"] = "value1";
                blob.Properties.CacheControl = "no-transform";
                blob.Properties.ContentDisposition = "attachment";
                blob.Properties.ContentEncoding = "gzip";
                blob.Properties.ContentLanguage = "tr,en";
                blob.Properties.ContentMD5 = md5;
                blob.Properties.ContentType = "text/html";
                CreateForTest(blob, 0, 0, false);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual(1, blob2.Metadata.Count);
                Assert.AreEqual("value1", blob2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1", blob2.Metadata["KEY1"]);
                Assert.AreEqual("no-transform", blob2.Properties.CacheControl);
                Assert.AreEqual("attachment", blob2.Properties.ContentDisposition);
                Assert.AreEqual("gzip", blob2.Properties.ContentEncoding);
                Assert.AreEqual("tr,en", blob2.Properties.ContentLanguage);
                Assert.AreEqual(md5, blob2.Properties.ContentMD5);
                Assert.AreEqual("text/html", blob2.Properties.ContentType);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify that empty metadata on a block blob can be retrieved.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobGetEmptyMetadata()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 0, 0, false);
                blob.Metadata["key1"] = "value1";

                OperationContext context = new OperationContext();
                context.SendingRequest += (sender, e) =>
                {
                    HttpRequestHandler.SetHeader(e.Request, "x-ms-meta-key1", String.Empty);
                };

                blob.SetMetadata(operationContext: context);
                blob2.FetchAttributes();
                Assert.AreEqual(1, blob2.Metadata.Count);
#if !NETCOREAPP2_0
                Assert.AreEqual(string.Empty, blob2.Metadata["key1"]);
#else
                //Headers in NetCore do not get overwritten. But headers are a list of values
                Assert.AreEqual("value1,", blob2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1,", blob2.Metadata["KEY1"]);
#endif

            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify that a block blob's metadata can be updated")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetMetadata()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 0, 0, false);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual(0, blob2.Metadata.Count);

                blob.Metadata["key1"] = null;
                StorageException e = TestHelper.ExpectedException<StorageException>(
                    () => blob.SetMetadata(),
                    "Metadata keys should have a non-null value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "";
                e = TestHelper.ExpectedException<StorageException>(
                    () => blob.SetMetadata(),
                    "Metadata keys should have a non-empty value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = " ";
                e = TestHelper.ExpectedException<StorageException>(
                    () => blob.SetMetadata(),
                    "Metadata keys should have a non-whitespace only value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "value1";
                blob.SetMetadata();

                blob2.FetchAttributes();
                Assert.AreEqual(1, blob2.Metadata.Count);
                Assert.AreEqual("value1", blob2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1", blob2.Metadata["KEY1"]);

                CloudBlockBlob blob3 = (CloudBlockBlob)container.ListBlobs(null, true, BlobListingDetails.Metadata, null, null).First();
                Assert.AreEqual(1, blob3.Metadata.Count);
                Assert.AreEqual("value1", blob3.Metadata["key1"]);
                Assert.AreEqual("value1", blob3.Metadata["KEY1"]);

                blob.Metadata.Clear();
                blob.SetMetadata();

                blob2.FetchAttributes();
                Assert.AreEqual(0, blob2.Metadata.Count);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Verify that a block blob's metadata can be updated")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetMetadataAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 0, 0, true);

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    IAsyncResult result = blob2.BeginFetchAttributes(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob2.EndFetchAttributes(result);
                    Assert.AreEqual(0, blob2.Metadata.Count);

                    blob.Metadata["key1"] = null;
                    result = blob.BeginSetMetadata(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Exception e = TestHelper.ExpectedException<StorageException>(
                        () => blob.EndSetMetadata(result),
                        "Metadata keys should have a non-null value");
                    Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                    blob.Metadata["key1"] = "";
                    result = blob.BeginSetMetadata(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    e = TestHelper.ExpectedException<StorageException>(
                        () => blob.EndSetMetadata(result),
                        "Metadata keys should have a non-empty value");
                    Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                    blob.Metadata["key1"] = " ";
                    result = blob.BeginSetMetadata(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    e = TestHelper.ExpectedException<StorageException>(
                        () => blob.EndSetMetadata(result),
                        "Metadata keys should have a non-whitespace only value");
                    Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                    blob.Metadata["key1"] = "" + "," + "";
                    blob.SetMetadataAsync().Wait();

                    blob2.FetchAttributesAsync().Wait();
                    Assert.AreEqual(1, blob2.Metadata.Count);
                    Assert.AreEqual(",", blob2.Metadata["key1"]);

                    blob.Metadata["key1"] = "value1";
                    result = blob.BeginSetMetadata(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob.EndSetMetadata(result);

                    result = blob2.BeginFetchAttributes(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob2.EndFetchAttributes(result);
                    Assert.AreEqual(1, blob2.Metadata.Count);
                    Assert.AreEqual("value1", blob2.Metadata["key1"]);
                    // Metadata keys should be case-insensitive
                    Assert.AreEqual("value1", blob2.Metadata["KEY1"]);

                    result = container.BeginListBlobsSegmented(null, true, BlobListingDetails.Metadata, null, null, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    BlobResultSegment results = container.EndListBlobsSegmented(result);
                    CloudBlockBlob blob3 = (CloudBlockBlob)results.Results.First();
                    Assert.AreEqual(1, blob3.Metadata.Count);
                    Assert.AreEqual("value1", blob3.Metadata["key1"]);
                    Assert.AreEqual("value1", blob3.Metadata["KEY1"]);

                    blob.Metadata.Clear();
                    result = blob.BeginSetMetadata(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob.EndSetMetadata(result);

                    result = blob2.BeginFetchAttributes(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob2.EndFetchAttributes(result);
                    Assert.AreEqual(0, blob2.Metadata.Count);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Verify that a block blob's metadata can be updated")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSetMetadataTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTestTask(blob, 0, 0);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual(0, blob2.Metadata.Count);

                blob.Metadata["key1"] = null;
                StorageException e = TestHelper.ExpectedExceptionTask<StorageException>(
                    blob.SetMetadataAsync(),
                    "Metadata keys should have a non-null value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "";
                e = TestHelper.ExpectedExceptionTask<StorageException>(
                    blob.SetMetadataAsync(),
                    "Metadata keys should have a non-empty value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = " ";
                e = TestHelper.ExpectedExceptionTask<StorageException>(
                    blob.SetMetadataAsync(),
                    "Metadata keys should have a non-whitespace only value");
                Assert.IsInstanceOfType(e.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "value1";
                blob.SetMetadataAsync().Wait();

                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual(1, blob2.Metadata.Count);
                Assert.AreEqual("value1", blob2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1", blob2.Metadata["KEY1"]);

                CloudBlockBlob blob3 =
                    (CloudBlockBlob)
                    container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.Metadata, null, null, null, null)
                             .Result
                             .Results
                             .First();

                Assert.AreEqual(1, blob3.Metadata.Count);
                Assert.AreEqual("value1", blob3.Metadata["key1"]);
                Assert.AreEqual("value1", blob3.Metadata["KEY1"]);

                blob.Metadata.Clear();
                blob.SetMetadataAsync().Wait();

                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual(0, blob2.Metadata.Count);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Upload blocks and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUpload()
        {
            CloudBlockBlobUpload(true, false);
        }

        [TestMethod]
        [Description("Upload blocks with non-seekable stream and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadWithNonSeekableStream()
        {
            CloudBlockBlobUpload(false, false);
        }

        [TestMethod]
        [Description("Upload blocks and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadAPM()
        {
            CloudBlockBlobUpload(true, true);
        }

        [TestMethod]
        [Description("Upload blocks with non-seekable stream and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadWithNonSeekableStreamAPM()
        {
            CloudBlockBlobUpload(false, true);
        }

        private void CloudBlockBlobUpload(bool seekableSourceStream, bool isAsync)
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                using (MemoryStream wholeBlob = new MemoryStream())
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        foreach (string block in blocks)
                        {
                            using (Stream memoryStream = seekableSourceStream ? new MemoryStream(buffer) : new NonSeekableMemoryStream(buffer))
                            {
                                if (isAsync)
                                {
                                    IAsyncResult result = blob.BeginPutBlock(block, memoryStream, null,
                                        ar => waitHandle.Set(),
                                        null);
                                    waitHandle.WaitOne();
                                    blob.EndPutBlock(result);
                                }
                                else
                                {
                                    blob.PutBlock(block, memoryStream, null);
                                }
                            }
                            wholeBlob.Write(buffer, 0, buffer.Length);
                        }

                        foreach (string block in extraBlocks)
                        {
                            using (Stream memoryStream = seekableSourceStream ? new MemoryStream(buffer) : new NonSeekableMemoryStream(buffer))
                            {
                                if (isAsync)
                                {
                                    IAsyncResult result = blob.BeginPutBlock(block, memoryStream, null,
                                        ar => waitHandle.Set(),
                                        null);
                                    waitHandle.WaitOne();
                                    blob.EndPutBlock(result);
                                }
                                else
                                {
                                    blob.PutBlock(block, memoryStream, null);
                                }
                            }
                        }

                        if (isAsync)
                        {
                            IAsyncResult result = blob.BeginPutBlockList(blocks,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndPutBlockList(result);
                        }
                        else
                        {
                            blob.PutBlockList(blocks);
                        }
                    }

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    using (MemoryStream downloadedBlob = new MemoryStream())
                    {
                        blob2.DownloadToStream(downloadedBlob);
                        TestHelper.AssertStreamsAreEqual(wholeBlob, downloadedBlob);
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Upload blocks and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadTask()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                using (MemoryStream wholeBlob = new MemoryStream())
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    foreach (string block in blocks)
                    {
                        using (Stream memoryStream = new MemoryStream(buffer))
                        {
                            blob.PutBlockAsync(block, memoryStream, null).Wait();
                        }
                        wholeBlob.Write(buffer, 0, buffer.Length);
                    }

                    foreach (string block in extraBlocks)
                    {
                        using (Stream memoryStream = new MemoryStream(buffer))
                        {
                            blob.PutBlockAsync(block, memoryStream, null).Wait();
                        }
                    }
                    blob.PutBlockListAsync(blocks).Wait();

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    using (MemoryStream downloadedBlob = new MemoryStream())
                    {
                        blob2.DownloadToStreamAsync(downloadedBlob).Wait();
                        TestHelper.AssertStreamsAreEqual(wholeBlob, downloadedBlob);
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Upload a block blob and then verify the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadBlockList()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                foreach (string block in blocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlock(block, memoryStream, null);
                    }
                }
                blob.PutBlockList(blocks);

                foreach (string block in extraBlocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlock(block, memoryStream, null);
                    }
                }

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual(1024 * blocks.Count, blob2.Properties.Length);

                IEnumerable<ListBlockItem> blockList = blob2.DownloadBlockList();
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsTrue(blockItem.Committed);
                    Assert.IsTrue(blocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, blocks.Count);

                blockList = blob2.DownloadBlockList(BlockListingFilter.Uncommitted, null, null, null);
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsFalse(blockItem.Committed);
                    Assert.IsTrue(extraBlocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, extraBlocks.Count);

                // Check with 0 length
                blocks = GetBlockIdList(0);
                blob.PutBlockList(blocks);

                blob.DownloadBlockList();
                Assert.AreEqual(0, blob.Properties.Length);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Upload a block blob and then verify the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadBlockListAPM()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                foreach (string block in blocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlock(block, memoryStream, null);
                    }
                }
                blob.PutBlockList(blocks);

                foreach (string block in extraBlocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlock(block, memoryStream, null);
                    }
                }

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributes();
                Assert.AreEqual(1024 * blocks.Count, blob2.Properties.Length);

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result = blob2.BeginDownloadBlockList(
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    IEnumerable<ListBlockItem> blockList = blob2.EndDownloadBlockList(result);
                    foreach (ListBlockItem blockItem in blockList)
                    {
                        Assert.IsTrue(blockItem.Committed);
                        Assert.IsTrue(blocks.Remove(blockItem.Name));
                    }
                    Assert.AreEqual(0, blocks.Count);

                    result = blob2.BeginDownloadBlockList(BlockListingFilter.Uncommitted, null, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blockList = blob2.EndDownloadBlockList(result);
                    foreach (ListBlockItem blockItem in blockList)
                    {
                        Assert.IsFalse(blockItem.Committed);
                        Assert.IsTrue(extraBlocks.Remove(blockItem.Name));
                    }
                    Assert.AreEqual(0, extraBlocks.Count);

                    // Check with 0 length
                    blocks = GetBlockIdList(0);
                    blob.PutBlockList(blocks);

                    result = blob.BeginDownloadBlockList(
                      ar => waitHandle.Set(),
                      null);
                    waitHandle.WaitOne();
                    blockList = blob.EndDownloadBlockList(result);
                    Assert.AreEqual(0, blob.Properties.Length);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Upload a block blob and then verify the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadBlockListTask()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                foreach (string block in blocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlockAsync(block, memoryStream, null).Wait();
                    }
                }
                blob.PutBlockListAsync(blocks).Wait();

                foreach (string block in extraBlocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        blob.PutBlockAsync(block, memoryStream, null).Wait();
                    }
                }

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                blob2.FetchAttributesAsync().Wait();
                Assert.AreEqual(1024 * blocks.Count, blob2.Properties.Length);

                IEnumerable<ListBlockItem> blockList = blob2.DownloadBlockListAsync().Result;
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsTrue(blockItem.Committed);
                    Assert.IsTrue(blocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, blocks.Count);

                blockList = blob2.DownloadBlockListAsync(BlockListingFilter.Uncommitted, null, null, null).Result;
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsFalse(blockItem.Committed);
                    Assert.IsTrue(extraBlocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, extraBlocks.Count);

                // Check with 0 length
                blocks = GetBlockIdList(0);
                blob.PutBlockListAsync(blocks).Wait();

                blockList = blob.DownloadBlockListAsync().Result;
                Assert.AreEqual(0, blob.Properties.Length);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadToStream()
        {
            byte[] buffer = GetRandomBuffer(1 * Constants.MB);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream originalBlob = new MemoryStream(buffer))
                {
                    blob.UploadFromStream(originalBlob);

                    using (MemoryStream downloadedBlob = new MemoryStream())
                    {
                        blob.DownloadToStream(downloadedBlob);
                        TestHelper.AssertStreamsAreEqual(originalBlob, downloadedBlob);
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadToStreamAPM()
        {
            byte[] buffer = GetRandomBuffer(1 * Constants.MB);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream originalBlob = new MemoryStream(buffer))
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        ICancellableAsyncResult result = blob.BeginUploadFromStream(originalBlob,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndUploadFromStream(result);

                        using (MemoryStream downloadedBlob = new MemoryStream())
                        {
                            result = blob.BeginDownloadToStream(downloadedBlob,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndDownloadToStream(result);
                            TestHelper.AssertStreamsAreEqual(originalBlob, downloadedBlob);
                        }
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadToStreamAPMOverload()
        {
            byte[] buffer = GetRandomBuffer(1 * Constants.MB);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream originalBlob = new MemoryStream(buffer))
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        ICancellableAsyncResult result = blob.BeginUploadFromStream(originalBlob,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndUploadFromStream(result);

                        using (MemoryStream downloadedBlob = new MemoryStream())
                        {
                            OperationContext context = new OperationContext();
                            result = blob.BeginDownloadRangeToStream(downloadedBlob,
                                0, /* offset */
                                buffer.Length, /* Length */
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndDownloadRangeToStream(result);
                            TestHelper.AssertStreamsAreEqual(originalBlob, downloadedBlob);
                        }
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadToStreamTask()
        {
            byte[] buffer = GetRandomBuffer(1 * Constants.MB);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream originalBlob = new MemoryStream(buffer))
                {
                    blob.UploadFromStreamAsync(originalBlob).Wait();

                    using (MemoryStream downloadedBlob = new MemoryStream())
                    {
                        blob.DownloadToStreamAsync(downloadedBlob).Wait();
                        TestHelper.AssertStreamsAreEqual(originalBlob, downloadedBlob);
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobDownloadToStreamOverloadTask()
        {
            byte[] buffer = GetRandomBuffer(1 * Constants.MB);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream originalBlob = new MemoryStream(buffer))
                {
                    blob.UploadFromStreamAsync(originalBlob).Wait();

                    using (MemoryStream downloadedBlob = new MemoryStream())
                    {
                        OperationContext context = new OperationContext();
                        blob.DownloadRangeToStreamAsync(downloadedBlob, 0, buffer.Length).Wait();
                        TestHelper.AssertStreamsAreEqual(originalBlob, downloadedBlob);
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync();
            }
        }
#endif

        [TestMethod]
        [Description("Single put blob with invalid options")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamInvalidOptions()
        {
            BlobRequestOptions options = new BlobRequestOptions()
            {
                UseTransactionalMD5 = true,
                StoreBlobContentMD5 = false,
            };

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream stream = new MemoryStream())
                {
                    TestHelper.ExpectedException<ArgumentException>(
                        () => blob.UploadFromStream(stream, null, options, null),
                        "Single put blob with mismatching MD5 options should fail immediately");
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Single put blob with invalid options")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamAPMInvalidOptions()
        {
            BlobRequestOptions options = new BlobRequestOptions()
            {
                UseTransactionalMD5 = true,
                StoreBlobContentMD5 = false,
            };

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (AutoResetEvent waitHandler = new AutoResetEvent(false))
                {
                    using (MemoryStream stream = new MemoryStream())
                    {
                        IAsyncResult result = blob.BeginUploadFromStream(stream, null, options, null, ar => waitHandler.Set(), null);
                        waitHandler.WaitOne();
                        TestHelper.ExpectedException<ArgumentException>(
                            () => blob.EndUploadFromStream(result),
                            "Single put blob with mismatching MD5 options should fail immediately");
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Single put blob with invalid options")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamInvalidOptionsTask()
        {
            BlobRequestOptions options = new BlobRequestOptions()
            {
                UseTransactionalMD5 = true,
                StoreBlobContentMD5 = false,
            };

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream stream = new MemoryStream())
                {
                    TestHelper.ExpectedException<ArgumentException>(
                        () => blob.UploadFromStreamAsync(stream, null, options, null).GetAwaiter().GetResult(),
                        "Single put blob with mismatching MD5 options should fail immediately");
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamWithAccessCondition()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                AccessCondition accessCondition = AccessCondition.GenerateIfNoneMatchCondition("*");
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, false, true);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);
                blob.FetchAttributes();
                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                TestHelper.ExpectedException(
                    () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, false, true),
                    "Uploading a blob on top of an existing blob should fail if the ETag matches",
                    HttpStatusCode.PreconditionFailed);
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, false, true);

                blob = container.GetBlockBlobReference("blob3");
                CreateForTest(blob, 1, 1024, false);
                blob.FetchAttributes();
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                TestHelper.ExpectedException(
                    () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, false, true),
                    "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                    HttpStatusCode.PreconditionFailed);
                TestHelper.ExpectedException(
                    () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, false, true),
                    "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                    HttpStatusCode.NotFound);
                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, false, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamAPMWithAccessCondition()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                AccessCondition accessCondition = AccessCondition.GenerateIfNoneMatchCondition("\"*\"");
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, true, true);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 1, 1024, false);
                blob.FetchAttributes();
                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                TestHelper.ExpectedException(
                    () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, true, true),
                    "Uploading a blob on top of an existing blob should fail if the ETag matches",
                    HttpStatusCode.PreconditionFailed);
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, true, true);

                blob = container.GetBlockBlobReference("blob3");
                CreateForTest(blob, 1, 1024, false);
                blob.FetchAttributes();
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                TestHelper.ExpectedException(
                   () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, true, true),
                   "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                   HttpStatusCode.PreconditionFailed);
                TestHelper.ExpectedException(
                    () => this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, true, true),
                    "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                    HttpStatusCode.NotFound);

                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 2 * Constants.MB, null, accessCondition, true, false, 0, true, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamWithNonSeekableStream()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, true, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, false, 1024, false, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamAPMWithNonSeekableStream()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, true, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, false, false, 1024, true, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStream()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, true, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, false, 1024, false, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, true, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, null, null, true, false, 1024, true, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamLength()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                // Upload 2MB of a 5MB stream
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, true, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, false, 1024, false, true);

                // Exclude last byte
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB - 1, null, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB - 1, null, true, true, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB - 1, null, true, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB - 1, null, true, false, 1024, false, true);

                // Upload exact amount
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB, null, true, true, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB, null, true, true, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB, null, true, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB, null, true, false, 1024, false, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamLengthAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                // Upload 2MB of a 5MB stream
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, true, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 2 * Constants.MB, null, true, false, 1024, true, true);

                // Exclude last byte
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB - 1, null, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB - 1, null, true, true, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB - 1, null, true, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB - 1, null, true, false, 1024, true, true);

                // Upload exact amount
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB, null, true, true, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB, null, true, true, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 5 * Constants.MB, null, true, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 5 * Constants.MB, 4 * Constants.MB, null, true, false, 1024, true, true);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamLengthInvalid()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, true, true, 0, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, true, true, 1024, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, false, true, 0, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, false, true, 1024, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, true, false, 0, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, true, false, 1024, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, false, false, 0, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, false, false, 1024, false, false),
                    "The given stream does not contain the requested number of bytes from its given position.");
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadFromStreamLengthInvalidAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, true, true, 0, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, true, true, 1024, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, false, true, 0, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, false, true, 1024, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, true, false, 0, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, true, false, 1024, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB + 1, null, false, false, 0, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");

                TestHelper.ExpectedException<ArgumentOutOfRangeException>(
                    () =>
                    this.CloudBlockBlobUploadFromStream(
                        container, 2 * Constants.MB, 2 * Constants.MB - 1023, null, false, false, 1024, true, false),
                    "The given stream does not contain the requested number of bytes from its given position.");
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Upload blob using multiple threads and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobParallelUploadFromStream()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.ServiceClient.DefaultRequestOptions.ParallelOperationThreadCount = 8;
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 16 * Constants.MB, null, null, true, false, 0, false, true);
                this.CloudBlockBlobUploadFromStream(container, 16 * Constants.MB, null, null, true, false, 1024, false, true);
                this.CloudBlockBlobUploadFromStream(container, 25 * Constants.MB, null, null, true, false, 0, false, false, 5 * Constants.MB, 4);
                this.CloudBlockBlobUploadFromStream(container, 25 * Constants.MB, null, null, true, false, 1024, false, false, 5 * Constants.MB, 4);
                this.CloudBlockBlobUploadFromStream(container, 32 * Constants.MB, null, null, true, false, 0, false, false, 10 * Constants.MB, 3);
                this.CloudBlockBlobUploadFromStream(container, 32 * Constants.MB, null, null, true, false, 1024, false, false, 10 * Constants.MB, 3);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Upload blob using multiple threads and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobParallelUploadFromStreamApm()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.ServiceClient.DefaultRequestOptions.ParallelOperationThreadCount = 8;
            container.Create();
            try
            {
                this.CloudBlockBlobUploadFromStream(container, 16 * Constants.MB, null, null, true, false, 0, true, true);
                this.CloudBlockBlobUploadFromStream(container, 16 * Constants.MB, null, null, true, false, 1024, true, true);
                this.CloudBlockBlobUploadFromStream(container, 25 * Constants.MB, null, null, true, false, 0, true, false, 5 * Constants.MB, 4);
                this.CloudBlockBlobUploadFromStream(container, 25 * Constants.MB, null, null, true, false, 1024, true, false, 5 * Constants.MB, 4);
                this.CloudBlockBlobUploadFromStream(container, 32 * Constants.MB, null, null, true, false, 0, true, false, 10 * Constants.MB, 3);
                this.CloudBlockBlobUploadFromStream(container, 32 * Constants.MB, null, null, true, false, 1024, true, false, 10 * Constants.MB, 3);
            }
            finally
            {
                container.Delete();
            }
        }

        [TestMethod]
        [Description("Upload blob using multiple threads and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobParallelUploadFromStreamAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.ServiceClient.DefaultRequestOptions.ParallelOperationThreadCount = 8;
            container.Create();
            try
            {
                await this.CloudBlockBlobUploadFromStreamAsync(container, 16 * Constants.MB, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsync(container, 16 * Constants.MB, null, null, true, false, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsync(container, 25 * Constants.MB, null, null, true, false, 0, false, 5 * Constants.MB, 4);
                await this.CloudBlockBlobUploadFromStreamAsync(container, 25 * Constants.MB, null, null, true, false, 1024, false, 5 * Constants.MB, 4);
                await this.CloudBlockBlobUploadFromStreamAsync(container, 32 * Constants.MB, null, null, true, false, 0, false, 10 * Constants.MB, 3);
                await this.CloudBlockBlobUploadFromStreamAsync(container, 32 * Constants.MB, null, null, true, false, 1024, false, 10 * Constants.MB, 3);
            }
            finally
            {
                await container.DeleteAsync();
            }
        }

        [TestMethod]
        [Description("Upload blob using multiple threads and get blob set via BlobRequestOptions")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore)]
        [TestCategory(TenantTypeCategory.DevFabric)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobParallelUploadFromStreamRequestOptions()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();

            try
            {
                const long Size = 20 * Constants.MB;
                byte[] buffer = GetRandomBuffer(Size);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.StreamWriteSizeInBytes = (int)(1 * Constants.MB);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob2");
                blob2.StreamWriteSizeInBytes = (int)(1 * Constants.MB);

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob3");
                blob3.StreamWriteSizeInBytes = (int)(1 * Constants.MB);

                CloudBlockBlob blob4 = container.GetBlockBlobReference("blob4");
                blob4.StreamWriteSizeInBytes = (int)(5 * Constants.MB);

                CloudBlockBlob blob5 = container.GetBlockBlobReference("blob5");
                blob5.StreamWriteSizeInBytes = (int)(7 * Constants.MB);

                using (MemoryStream originalBlobStream = new MemoryStream())
                {
                    originalBlobStream.Write(buffer, 0, buffer.Length);

                    Stream sourceStream = new MemoryStream(buffer);
                    sourceStream.Seek(0, SeekOrigin.Begin);

                    using (sourceStream)
                    {
                        BlobRequestOptions options = new BlobRequestOptions()
                        {
                            StoreBlobContentMD5 = true,
                            SingleBlobUploadThresholdInBytes = Size,
                            ParallelOperationThreadCount = 2
                        };
                        OperationContext context = new OperationContext();
                        blob.UploadFromStream(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 21 since StreamWriteSizeInBytes is 1 MB
                        Assert.IsTrue(context.RequestResults.Count >= 21);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        options = new BlobRequestOptions()
                        {
                            StoreBlobContentMD5 = true,
                            SingleBlobUploadThresholdInBytes = Size / 2,
                            ParallelOperationThreadCount = 1
                        };
                        context = new OperationContext();
                        blob2.UploadFromStream(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 21 since StreamWriteSizeInBytes is 1 MB
                        Assert.IsTrue(context.RequestResults.Count >= 21);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        options = new BlobRequestOptions()
                        {
                            StoreBlobContentMD5 = true,
                            SingleBlobUploadThresholdInBytes = Size,
                            ParallelOperationThreadCount = 1
                        };
                        context = new OperationContext();
                        blob3.UploadFromStream(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should 1, or 2 if there is a retry
                        Assert.IsTrue(context.RequestResults.Count <= 2);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        options = new BlobRequestOptions()
                        {
                            StoreBlobContentMD5 = false,
                            SingleBlobUploadThresholdInBytes = Size,
                            ParallelOperationThreadCount = 3
                        };
                        context = new OperationContext();
                        blob4.UploadFromStream(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 5 since StreamWriteSizeInBytes is 5 MB
                        Assert.IsTrue(context.RequestResults.Count >= 5);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        context = new OperationContext();
                        blob5.UploadFromStream(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 4 since StreamWriteSizeInBytes is 7 MB
                        Assert.IsTrue(context.RequestResults.Count >= 4);
                    }
                }
            }
            finally
            {
                container.Delete();
            }
        }

        private void CloudBlockBlobUploadFromStream(CloudBlobContainer container, long size, long? copyLength, AccessCondition accessCondition, bool seekableSourceStream, bool allowSinglePut, int startOffset, bool isAsync, bool testMd5, long streamWriteSize = Constants.DefaultWriteBlockSizeBytes, int? parallelOperations = null)
        {
            byte[] buffer = GetRandomBuffer(size);

            MD5 hasher = MD5.Create();

            string md5 = string.Empty;
            if (testMd5)
            {
                md5 = Convert.ToBase64String(hasher.ComputeHash(buffer, startOffset, copyLength.HasValue ? (int)copyLength : buffer.Length - startOffset));
            }

            CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
            blob.ServiceClient.DefaultRequestOptions.SingleBlobUploadThresholdInBytes = allowSinglePut ? buffer.Length : buffer.Length / 2;
            blob.StreamWriteSizeInBytes = (int)streamWriteSize;

            BlobRequestOptions options = new BlobRequestOptions()
            {
                StoreBlobContentMD5 = testMd5
            };

            if (parallelOperations.HasValue)
            {
                options.ParallelOperationThreadCount = parallelOperations;
            }

            using (MemoryStream originalBlobStream = new MemoryStream())
            {
                originalBlobStream.Write(buffer, startOffset, buffer.Length - startOffset);

                Stream sourceStream;
                if (seekableSourceStream)
                {
                    MemoryStream stream = new MemoryStream(buffer);
                    stream.Seek(startOffset, SeekOrigin.Begin);
                    sourceStream = stream;
                }
                else
                {
                    NonSeekableMemoryStream stream = new NonSeekableMemoryStream(buffer);
                    stream.ForceSeek(startOffset, SeekOrigin.Begin);
                    sourceStream = stream;
                }

                using (sourceStream)
                {
                    if (isAsync)
                    {                    
                            using (ManualResetEvent waitHandle = new ManualResetEvent(false))
                            {
                                if (copyLength.HasValue)
                                {
                                    ICancellableAsyncResult result = blob.BeginUploadFromStream(
                                        sourceStream, copyLength.Value, accessCondition, options, null,
                                        ar => waitHandle.Set(), null);
                                    waitHandle.WaitOne();
                                    blob.EndUploadFromStream(result);
                                }
                                else
                                {
                                    ICancellableAsyncResult result = blob.BeginUploadFromStream(
                                        sourceStream, accessCondition, options, null, ar => waitHandle.Set(), null);
                                    waitHandle.WaitOne();
                                    blob.EndUploadFromStream(result);
                                }
                            }     
                    }
                    else
                    {
                        if (copyLength.HasValue)
                        {
                            blob.UploadFromStream(sourceStream, copyLength.Value, accessCondition, options);
                        }
                        else
                        {
                            blob.UploadFromStream(sourceStream, accessCondition, options);
                        }
                    }
                }

                blob.FetchAttributes();

                if (testMd5)
                {
                    Assert.AreEqual(md5, blob.Properties.ContentMD5);
                }

                using (MemoryStream downloadedBlobStream = new MemoryStream())
                {
                    if (isAsync)
                    {
                        using (ManualResetEvent waitHandle = new ManualResetEvent(false))
                        {
                            ICancellableAsyncResult result = blob.BeginDownloadToStream(
                                downloadedBlobStream, ar => waitHandle.Set(), null);
                            waitHandle.WaitOne();
                            blob.EndDownloadToStream(result);
                        }
                    }
                    else
                    {
                        blob.DownloadToStream(downloadedBlobStream);
                    }

                    Assert.AreEqual(copyLength ?? originalBlobStream.Length, downloadedBlobStream.Length);
                    TestHelper.AssertStreamsAreEqualAtIndex(
                        originalBlobStream,
                        downloadedBlobStream,
                        0,
                        0,
                        copyLength.HasValue ? (int)copyLength : (int)originalBlobStream.Length);
                }
            }

            blob.Delete();
        }

        private async Task CloudBlockBlobUploadFromStreamAsync(CloudBlobContainer container, long size, long? copyLength, AccessCondition accessCondition, bool seekableSourceStream, bool allowSinglePut, int startOffset,  bool testMd5, long streamWriteSize = Constants.DefaultWriteBlockSizeBytes, int? parallelOperations = null)
        {
            byte[] buffer = GetRandomBuffer(size);
            string md5 = string.Empty;
            if (testMd5)
            {
                MD5 hasher = MD5.Create();
                md5 = Convert.ToBase64String(hasher.ComputeHash(buffer, startOffset, copyLength.HasValue ? (int)copyLength : buffer.Length - startOffset));
            }
          
            CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
            blob.ServiceClient.DefaultRequestOptions.SingleBlobUploadThresholdInBytes = allowSinglePut ? buffer.Length : buffer.Length / 2;
            BlobRequestOptions options = new BlobRequestOptions()
            {
                StoreBlobContentMD5 = testMd5
            };

            OperationContext operationContext = new OperationContext();

            if (parallelOperations.HasValue)
            {
                options.ParallelOperationThreadCount = parallelOperations;
            }

            blob.StreamWriteSizeInBytes = (int) streamWriteSize;

            using (MemoryStream originalBlobStream = new MemoryStream())
            {
                originalBlobStream.Write(buffer, startOffset, buffer.Length - startOffset);

                Stream sourceStream;
                if (seekableSourceStream)
                {
                    MemoryStream stream = new MemoryStream(buffer);
                    stream.Seek(startOffset, SeekOrigin.Begin);
                    sourceStream = stream;
                }
                else
                {
                    NonSeekableMemoryStream stream = new NonSeekableMemoryStream(buffer);
                    stream.ForceSeek(startOffset, SeekOrigin.Begin);
                    sourceStream = stream;
                }

                using (sourceStream)
                {
                    if (copyLength.HasValue)
                    {
                        await blob.UploadFromStreamAsync(sourceStream, copyLength.Value, accessCondition, options, operationContext);
                    }
                    else
                    {
                        await blob.UploadFromStreamAsync(sourceStream, accessCondition, options, operationContext);
                    }
                }

                await blob.FetchAttributesAsync();
                if (testMd5)
                {
                    Assert.AreEqual(md5, blob.Properties.ContentMD5);
                }

                using (MemoryStream downloadedBlobStream = new MemoryStream())
                {
                    await blob.DownloadToStreamAsync(downloadedBlobStream);
                    Assert.AreEqual(copyLength ?? originalBlobStream.Length, downloadedBlobStream.Length);
                    TestHelper.AssertStreamsAreEqualAtIndex(
                        originalBlobStream,
                        downloadedBlobStream,
                        0,
                        0,
                        copyLength.HasValue ? (int)copyLength : (int)originalBlobStream.Length);
                }
            }
        }

        [TestMethod]
        [Description("Create snapshots of a block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshot()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                MemoryStream originalData = new MemoryStream(GetRandomBuffer(1024));
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.UploadFromStream(originalData);
                Assert.IsFalse(blob.IsSnapshot);
                Assert.IsNull(blob.SnapshotTime, "Root blob has SnapshotTime set");
                Assert.IsFalse(blob.SnapshotQualifiedUri.Query.Contains("snapshot"));
                Assert.AreEqual(blob.Uri, blob.SnapshotQualifiedUri);

                CloudBlockBlob snapshot1 = blob.CreateSnapshot();
                Assert.AreEqual(blob.Properties.ETag, snapshot1.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, snapshot1.Properties.LastModified);
                Assert.IsTrue(snapshot1.IsSnapshot);
                Assert.IsNotNull(snapshot1.SnapshotTime, "Snapshot does not have SnapshotTime set");
                Assert.AreEqual(blob.Uri, snapshot1.Uri);
                Assert.AreNotEqual(blob.SnapshotQualifiedUri, snapshot1.SnapshotQualifiedUri);
                Assert.AreNotEqual(snapshot1.Uri, snapshot1.SnapshotQualifiedUri);
                Assert.IsTrue(snapshot1.SnapshotQualifiedUri.Query.Contains("snapshot"));

                CloudBlockBlob snapshot2 = blob.CreateSnapshot();
                Assert.IsTrue(snapshot2.SnapshotTime.Value > snapshot1.SnapshotTime.Value);

                snapshot1.FetchAttributes();
                snapshot2.FetchAttributes();
                blob.FetchAttributes();
                AssertAreEqual(snapshot1.Properties, blob.Properties);

                CloudBlockBlob snapshot1Clone = new CloudBlockBlob(new Uri(blob.Uri + "?snapshot=" + snapshot1.SnapshotTime.Value.ToString("O")), blob.ServiceClient.Credentials);
                Assert.IsNotNull(snapshot1Clone.SnapshotTime, "Snapshot clone does not have SnapshotTime set");
                Assert.AreEqual(snapshot1.SnapshotTime.Value, snapshot1Clone.SnapshotTime.Value);
                snapshot1Clone.FetchAttributes();
                AssertAreEqual(snapshot1.Properties, snapshot1Clone.Properties);

                CloudBlockBlob snapshotCopy = container.GetBlockBlobReference("blob2");
                snapshotCopy.StartCopy(TestHelper.Defiddler(snapshot1.Uri));
                WaitForCopy(snapshotCopy);
                Assert.AreEqual(CopyStatus.Success, snapshotCopy.CopyState.Status);

                TestHelper.ExpectedException<InvalidOperationException>(
                    () => snapshot1.OpenWrite(),
                    "Trying to write to a blob snapshot should fail");

                using (Stream snapshotStream = snapshot1.OpenRead())
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                blob.PutBlockList(new List<string>());
                blob.FetchAttributes();

                using (Stream snapshotStream = snapshot1.OpenRead())
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                List<IListBlobItem> blobs = container.ListBlobs(null, true, BlobListingDetails.All, null, null).ToList();
                Assert.AreEqual(4, blobs.Count);
                AssertAreEqual(snapshot1, (CloudBlob)blobs[0]);
                AssertAreEqual(snapshot2, (CloudBlob)blobs[1]);
                AssertAreEqual(blob, (CloudBlob)blobs[2]);
                AssertAreEqual(snapshotCopy, (CloudBlob)blobs[3]);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Create snapshots of a block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshotAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                MemoryStream originalData = new MemoryStream(GetRandomBuffer(1024));
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                IAsyncResult result;
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    result = blob.BeginUploadFromStream(originalData, ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    blob.EndUploadFromStream(result);
                    Assert.IsFalse(blob.IsSnapshot);
                    Assert.IsNull(blob.SnapshotTime, "Root blob has SnapshotTime set");
                    Assert.IsFalse(blob.SnapshotQualifiedUri.Query.Contains("snapshot"));
                    Assert.AreEqual(blob.Uri, blob.SnapshotQualifiedUri);

                    result = blob.BeginCreateSnapshot(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    CloudBlockBlob snapshot1 = blob.EndCreateSnapshot(result);
                    Assert.AreEqual(blob.Properties.ETag, snapshot1.Properties.ETag);
                    Assert.AreEqual(blob.Properties.LastModified, snapshot1.Properties.LastModified);
                    Assert.IsTrue(snapshot1.IsSnapshot);
                    Assert.IsNotNull(snapshot1.SnapshotTime, "Snapshot does not have SnapshotTime set");
                    Assert.AreEqual(blob.Uri, snapshot1.Uri);
                    Assert.AreNotEqual(blob.SnapshotQualifiedUri, snapshot1.SnapshotQualifiedUri);
                    Assert.AreNotEqual(snapshot1.Uri, snapshot1.SnapshotQualifiedUri);
                    Assert.IsTrue(snapshot1.SnapshotQualifiedUri.Query.Contains("snapshot"));

                    result = blob.BeginCreateSnapshot(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    CloudBlockBlob snapshot2 = blob.EndCreateSnapshot(result);
                    Assert.IsTrue(snapshot2.SnapshotTime.Value > snapshot1.SnapshotTime.Value);

                    snapshot1.FetchAttributes();
                    snapshot2.FetchAttributes();
                    blob.FetchAttributes();
                    AssertAreEqual(snapshot1.Properties, blob.Properties);

                    CloudBlockBlob snapshotCopy = container.GetBlockBlobReference("blob2");
                    result = snapshotCopy.BeginStartCopy(snapshot1, null, null, null, null, ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    snapshotCopy.EndStartCopy(result);
                    WaitForCopy(snapshotCopy);
                    Assert.AreEqual(CopyStatus.Success, snapshotCopy.CopyState.Status);

                    result = snapshot1.BeginOpenWrite(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    TestHelper.ExpectedException<InvalidOperationException>(
                        () => snapshot1.EndOpenWrite(result),
                        "Trying to write to a blob snapshot should fail");
                    result = snapshot2.BeginOpenWrite(null, null, null, ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    TestHelper.ExpectedException<InvalidOperationException>(
                        () => snapshot2.EndOpenWrite(result),
                        "Trying to write to a blob snapshot should fail");

                    result = snapshot1.BeginOpenRead(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    using (Stream snapshotStream = snapshot1.EndOpenRead(result))
                    {
                        snapshotStream.Seek(0, SeekOrigin.End);
                        TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                    }

                    result = blob.BeginPutBlockList(new List<string>(), ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    blob.EndPutBlockList(result);

                    result = blob.BeginFetchAttributes(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    blob.EndFetchAttributes(result);

                    result = snapshot1.BeginOpenRead(ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    using (Stream snapshotStream = snapshot1.EndOpenRead(result))
                    {
                        snapshotStream.Seek(0, SeekOrigin.End);
                        TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                    }

                    List<IListBlobItem> blobs = container.ListBlobs(null, true, BlobListingDetails.All, null, null).ToList();
                    Assert.AreEqual(4, blobs.Count);
                    AssertAreEqual(snapshot1, (CloudBlob)blobs[0]);
                    AssertAreEqual(snapshot2, (CloudBlob)blobs[1]);
                    AssertAreEqual(blob, (CloudBlob)blobs[2]);
                    AssertAreEqual(snapshotCopy, (CloudBlob)blobs[3]);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Create snapshots of a block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshotTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                MemoryStream originalData = new MemoryStream(GetRandomBuffer(1024));
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                blob.UploadFromStreamAsync(originalData).Wait();
                Assert.IsFalse(blob.IsSnapshot);
                Assert.IsNull(blob.SnapshotTime, "Root blob has SnapshotTime set");
                Assert.IsFalse(blob.SnapshotQualifiedUri.Query.Contains("snapshot"));
                Assert.AreEqual(blob.Uri, blob.SnapshotQualifiedUri);

                CloudBlockBlob snapshot1 = blob.CreateSnapshotAsync().Result;
                Assert.AreEqual(blob.Properties.ETag, snapshot1.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, snapshot1.Properties.LastModified);
                Assert.IsTrue(snapshot1.IsSnapshot);
                Assert.IsNotNull(snapshot1.SnapshotTime, "Snapshot does not have SnapshotTime set");
                Assert.AreEqual(blob.Uri, snapshot1.Uri);
                Assert.AreNotEqual(blob.SnapshotQualifiedUri, snapshot1.SnapshotQualifiedUri);
                Assert.AreNotEqual(snapshot1.Uri, snapshot1.SnapshotQualifiedUri);
                Assert.IsTrue(snapshot1.SnapshotQualifiedUri.Query.Contains("snapshot"));

                CloudBlockBlob snapshot2 = blob.CreateSnapshotAsync().Result;
                Assert.IsTrue(snapshot2.SnapshotTime.Value > snapshot1.SnapshotTime.Value);

                snapshot1.FetchAttributesAsync().Wait();
                snapshot2.FetchAttributesAsync().Wait();
                blob.FetchAttributesAsync().Wait();
                AssertAreEqual(snapshot1.Properties, blob.Properties);

                CloudBlockBlob snapshotCopy = container.GetBlockBlobReference("blob2");
                snapshotCopy.StartCopyAsync(snapshot1, null, null, null, null).Wait();
                bool copyInProgress = true;
                while (copyInProgress)
                {
                    Thread.Sleep(1000);
                    snapshotCopy.FetchAttributesAsync().Wait();
                    copyInProgress = (snapshotCopy.CopyState.Status == CopyStatus.Pending);
                }
                Assert.AreEqual(CopyStatus.Success, snapshotCopy.CopyState.Status);

                TestHelper.ExpectedException<InvalidOperationException>(
                    () => snapshot1.OpenWriteAsync().GetAwaiter().GetResult(),
                    "Trying to write to a blob snapshot should fail");

                TestHelper.ExpectedException<InvalidOperationException>(
                    () => snapshot2.OpenWriteAsync(null, null, null).GetAwaiter().GetResult(),
                    "Trying to write to a blob snapshot should fail");

                using (Stream snapshotStream = snapshot1.OpenReadAsync().Result)
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                blob.PutBlockListAsync(new List<string>()).Wait();

                blob.FetchAttributesAsync().Wait();

                using (Stream snapshotStream = snapshot1.OpenReadAsync().Result)
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                List<IListBlobItem> blobs =
                    container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.All, null, null, null, null)
                             .Result
                             .Results
                             .ToList();

                Assert.AreEqual(4, blobs.Count);
                AssertAreEqual(snapshot1, (CloudBlob)blobs[0]);
                AssertAreEqual(snapshot2, (CloudBlob)blobs[1]);
                AssertAreEqual(blob, (CloudBlob)blobs[2]);
                AssertAreEqual(snapshotCopy, (CloudBlob)blobs[3]);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Download range greater than the size of the blob.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore)]
        [TestCategory(TenantTypeCategory.DevFabric)]
        [TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlobExceedMaxRange()
        {
            CloudBlobContainer container = GetRandomContainerReference();

            try
            {
                container.Create();
                CloudBlockBlob blob = container.GetBlockBlobReference("blockblob");

                string message = "Sample initial text";
                blob.UploadText(message);

                byte[] buffer = new byte[message.Length];
                blob.DownloadRangeToByteArray(buffer, 0, 0, message.Length + 5);
                string actualMessage = System.Text.Encoding.Default.GetString(buffer);
                Assert.AreEqual(message, actualMessage);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Create a snapshot with explicit metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshotMetadata()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 2, 1024, false);

                blob.Metadata["Hello"] = "World";
                blob.Metadata["Marco"] = "Polo";
                blob.SetMetadata();

                IDictionary<string, string> snapshotMetadata = new Dictionary<string, string>();
                snapshotMetadata["Hello"] = "Dolly";
                snapshotMetadata["Yoyo"] = "Ma";

                CloudBlockBlob snapshot = blob.CreateSnapshot(snapshotMetadata);

                // Test the client view against the expected metadata
                // Metadata keys should be case-insensitive
                // None of the original metadata should be present
                Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));

                // Test the server view against the expected metadata
                snapshot.FetchAttributes();
                Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Create a snapshot with explicit metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshotMetadataAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 2, 1024, false);

                blob.Metadata["Hello"] = "World";
                blob.Metadata["Marco"] = "Polo";
                blob.SetMetadata();

                IDictionary<string, string> snapshotMetadata = new Dictionary<string, string>();
                snapshotMetadata["Hello"] = "Dolly";
                snapshotMetadata["Yoyo"] = "Ma";

                IAsyncResult result;
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    OperationContext context = new OperationContext();
                    result = blob.BeginCreateSnapshot(snapshotMetadata, null, null, context, ar => waitHandle.Set(), null);
                    waitHandle.WaitOne();
                    CloudBlockBlob snapshot = blob.EndCreateSnapshot(result);

                    // Test the client view against the expected metadata
                    // Metadata keys should be case-insensitive
                    // None of the original metadata should be present
                    Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                    Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                    Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                    Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));

                    // Test the server view against the expected metadata
                    snapshot.FetchAttributes();
                    Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                    Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                    Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                    Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Create a snapshot with explicit metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobSnapshotMetadataTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTestTask(blob, 2, 1024);

                blob.Metadata["Hello"] = "World";
                blob.Metadata["Marco"] = "Polo";
                blob.SetMetadataAsync().Wait();

                IDictionary<string, string> snapshotMetadata = new Dictionary<string, string>();
                snapshotMetadata["Hello"] = "Dolly";
                snapshotMetadata["Yoyo"] = "Ma";

                CloudBlockBlob snapshot = blob.CreateSnapshotAsync(snapshotMetadata, null, null, null).Result;

                // Test the client view against the expected metadata
                // Metadata keys should be case-insensitive
                // None of the original metadata should be present
                Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));

                // Test the server view against the expected metadata
                snapshot.FetchAttributesAsync().Wait();
                Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Set standard blob tier and fetch attributes")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public void CloudBlockBlobSetStandardBlobTier()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                foreach (RehydratePriority? rehydratePriority in new[] { default(RehydratePriority?), RehydratePriority.Standard, RehydratePriority.High })
                foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                {
                    if (blobTier == StandardBlobTier.Unknown)
                    {
                        continue;
                    }

                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    CreateForTestTask(blob, 2, 1024);
                    blob.FetchAttributes();
                    Assert.IsTrue(blob.Properties.StandardBlobTier.HasValue);
                    Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsTrue(blob.Properties.BlobTierInferred.Value);

                    CloudBlockBlob listBlob = (CloudBlockBlob)container.ListBlobs().ToList().First();
                    Assert.IsTrue(listBlob.Properties.StandardBlobTier.HasValue);
                    Assert.IsFalse(listBlob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsTrue(listBlob.Properties.BlobTierInferred.Value);

                    blob.SetStandardBlobTier(blobTier, rehydratePriority: rehydratePriority);
                    Assert.AreEqual(blobTier, blob.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob.Properties.RehydrationStatus.HasValue);
                    Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob.Properties.BlobTierInferred.Value);

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    blob2.FetchAttributes();
                    Assert.AreEqual(blobTier, blob2.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob2.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob2.Properties.RehydrationStatus.HasValue);
                    Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob2.Properties.BlobTierInferred.Value);

                    CloudBlockBlob blob3 = (CloudBlockBlob)container.ListBlobs().ToList().First();
                    Assert.AreEqual(blobTier, blob3.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob3.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob3.Properties.RehydrationStatus.HasValue);
                    Assert.IsTrue(blob3.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob3.Properties.BlobTierInferred.HasValue);

                    blob.Delete();
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier to archive then rehydrate it to hot and cool")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public void CloudBlockBlobRehydrateBlob()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTestTask(blob, 2, 1024);
                Assert.IsFalse(blob.Properties.BlobTierInferred.HasValue);
                Assert.IsFalse(blob.Properties.StandardBlobTier.HasValue);
                blob.FetchAttributes();
                Assert.IsTrue(blob.Properties.BlobTierInferred.HasValue);
                Assert.IsTrue(blob.Properties.StandardBlobTier.HasValue);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                blob.SetStandardBlobTier(StandardBlobTier.Archive);
                Assert.IsNull(blob.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob2");
                CreateForTestTask(blob2, 2, 1024);
                blob2.SetStandardBlobTier(StandardBlobTier.Archive);
                Assert.IsNull(blob2.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob2.Properties.StandardBlobTier.Value);
                Assert.IsFalse(blob2.Properties.BlobTierLastModifiedTime.HasValue);

                blob.SetStandardBlobTier(StandardBlobTier.Cool);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsNull(blob2.Properties.RehydrationStatus);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);
                blob.FetchAttributes();
                Assert.AreEqual(RehydrationStatus.PendingToCool, blob.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsTrue(blob.Properties.BlobTierLastModifiedTime.HasValue);

                blob2.SetStandardBlobTier(StandardBlobTier.Hot);
                Assert.AreEqual(StandardBlobTier.Archive, blob2.Properties.StandardBlobTier.Value);
                Assert.IsNull(blob2.Properties.RehydrationStatus);
                Assert.IsFalse(blob2.Properties.BlobTierLastModifiedTime.HasValue);
                blob2.FetchAttributes();
                Assert.AreEqual(RehydrationStatus.PendingToHot, blob2.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob2.Properties.StandardBlobTier.Value);
                Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);

                CloudBlockBlob listBlob = (CloudBlockBlob)container.ListBlobs().ToList().ElementAt(0);
                Assert.AreEqual(StandardBlobTier.Archive, listBlob.Properties.StandardBlobTier.Value);
                Assert.IsFalse(listBlob.Properties.PremiumPageBlobTier.HasValue);
                Assert.AreEqual(RehydrationStatus.PendingToCool, listBlob.Properties.RehydrationStatus.Value);
                Assert.IsTrue(listBlob.Properties.BlobTierLastModifiedTime.HasValue);
                Assert.AreEqual(listBlob.Properties.BlobTierLastModifiedTime.Value, blob.Properties.BlobTierLastModifiedTime.Value);

                CloudBlockBlob listBlob2 = (CloudBlockBlob)container.ListBlobs().ToList().ElementAt(1);
                Assert.AreEqual(StandardBlobTier.Archive, listBlob2.Properties.StandardBlobTier.Value);
                Assert.IsFalse(listBlob2.Properties.PremiumPageBlobTier.HasValue);
                Assert.AreEqual(RehydrationStatus.PendingToHot, listBlob2.Properties.RehydrationStatus.Value);
                Assert.AreEqual(listBlob2.Properties.BlobTierLastModifiedTime.Value, blob2.Properties.BlobTierLastModifiedTime.Value);

                blob.Delete();
                blob2.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier and fetch attributes")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public void CloudBlockBlobSetBlobTierAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                IAsyncResult result;
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                    {
                        if (blobTier == StandardBlobTier.Unknown)
                        {
                            continue;
                        }

                        CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                        CreateForTest(blob, 0, 0, true);

                        result = blob.BeginSetStandardBlobTier(blobTier, ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        blob.EndSetStandardBlobTier(result);
                        Assert.AreEqual(blobTier, blob.Properties.StandardBlobTier.Value);
                        Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                        Assert.IsFalse(blob.Properties.RehydrationStatus.HasValue);
                        Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                        CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                        result = blob2.BeginFetchAttributes(ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        blob2.EndFetchAttributes(result);
                        Assert.AreEqual(blobTier, blob2.Properties.StandardBlobTier.Value);
                        Assert.IsFalse(blob2.Properties.PremiumPageBlobTier.HasValue);
                        Assert.IsFalse(blob2.Properties.RehydrationStatus.HasValue);
                        Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);

                        blob.Delete();
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Set standard blob tier and fetch attributes")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public async Task CloudBlockBlobSetStandardBlobTierTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                foreach (RehydratePriority? rehydratePriority in new[] { default(RehydratePriority?), RehydratePriority.Standard, RehydratePriority.High })
                foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                {
                    if (blobTier == StandardBlobTier.Unknown)
                    {
                        continue;
                    }

                    string blobName = GetRandomBlobName();
                    CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
                    CreateForTest(blob, 0, 0, true);


                    await blob.SetStandardBlobTierAsync(blobTier, rehydratePriority, null, null, null, CancellationToken.None);
                    Assert.AreEqual(blobTier, blob.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob.Properties.RehydrationStatus.HasValue);
                    Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                    CloudBlockBlob blob2 = container.GetBlockBlobReference(blobName);
                    await blob2.FetchAttributesAsync();
                    Assert.AreEqual(blobTier, blob2.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob2.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob2.Properties.RehydrationStatus.HasValue);
                    Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);
                }
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }
#endif
        [TestMethod]
        [Description("Set standard blob tier when copying from an existing blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public void CloudBlockBlobSetStandardBlobTierOnCopy()
        {
            CloudBlobContainer container = GetRandomContainerReference();

            try
            {
                container.Create();

                foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                {
                    if(blobTier == StandardBlobTier.Unknown || blobTier == StandardBlobTier.Archive)
                    {
                        continue;
                    }

                    //random blob size and count calculation
                    Random rand = new Random();
                    int randomRangeSize = 500;
                    int randomRangeCount = 10;
                    int sourceSize = rand.Next(0, randomRangeSize) + 1024;
                    int sourceBlockCount = rand.Next(1, randomRangeCount);

                    CloudBlockBlob source = container.GetBlockBlobReference("source");
                    CreateForTest(source, sourceBlockCount, sourceSize, true);

                    //copy to another blockblob
                    CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                    string copyId = copy.StartCopy(TestHelper.Defiddler(source), blobTier);
                    copy.FetchAttributes();

                    Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);
                    Assert.AreEqual(blobTier, copy.Properties.StandardBlobTier);
                    Assert.IsFalse(copy.Properties.PremiumPageBlobTier.HasValue);
                    WaitForCopy(copy);

                    CloudBlockBlob copyRef = container.GetBlockBlobReference("copy");
                    copyRef.FetchAttributes();
                    Assert.IsFalse(copyRef.Properties.PremiumPageBlobTier.HasValue);
                    Assert.AreEqual(blobTier, copyRef.Properties.StandardBlobTier);

                    Assert.AreEqual(sourceSize * sourceBlockCount, copy.Properties.Length);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier when copying from an existing blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public void CloudBlockBlobSetStandardBlobTierOnCopyAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();

            try
            {
                container.Create();

                IAsyncResult result;

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                    {
                        if (blobTier == StandardBlobTier.Unknown || blobTier == StandardBlobTier.Archive)
                        {
                            continue;
                        }

                        //random blob size
                        Random rand = new Random();
                        int randomRangeSize = 500;
                        int randomRangeCount = 10;
                        int sourceSize = rand.Next(0, randomRangeSize) + 1024;
                        int sourceBlockCount = rand.Next(1, randomRangeCount);

                        CloudBlockBlob source = container.GetBlockBlobReference("source");
                        CreateForTest(source, sourceBlockCount, sourceSize, true);

                        //copy to another blockblob
                        CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                        result = copy.BeginStartCopy(TestHelper.Defiddler(source), blobTier,null, null, null, null,  ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        copy.EndStartCopy(result);

                        result = copy.BeginFetchAttributes(ar =>waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        copy.EndFetchAttributes(result);

                        Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);
                        Assert.AreEqual(blobTier, copy.Properties.StandardBlobTier.Value);
                        Assert.IsFalse(copy.Properties.PremiumPageBlobTier.HasValue);

                        Assert.AreEqual(sourceSize * sourceBlockCount, copy.Properties.Length);
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }
#if TASK
        [TestMethod]
        [Description("Set standard blob tier when copying from an existing blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public async Task CloudBlockBlobSetStandardBlobTierOnCopyTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();

            try
            {
                container.CreateAsync().Wait();

                foreach (StandardBlobTier blobTier in Enum.GetValues(typeof(StandardBlobTier)))
                {
                    if (blobTier == StandardBlobTier.Unknown || blobTier == StandardBlobTier.Archive)
                    {
                        continue;
                    }

                    //random blob size
                    Random rand = new Random();
                    int randomRangeSize = 500;
                    int randomRangeCount = 10;
                    int sourceSize = rand.Next(0, randomRangeSize) + 1024;
                    int sourceBlockCount = rand.Next(1, randomRangeCount);

                    CloudBlockBlob source = container.GetBlockBlobReference("source");
                    CreateForTest(source, sourceBlockCount, sourceSize, true);

                    //copy to another blockblob
                    CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                    copy.StartCopyAsync(TestHelper.Defiddler(source), blobTier, null, null, null, null, null, CancellationToken.None).Wait();
                    await copy.FetchAttributesAsync();
                    Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);
                    Assert.AreEqual(blobTier, copy.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(copy.Properties.PremiumPageBlobTier.HasValue);

                    CloudBlockBlob copy2 = container.GetBlockBlobReference("copy");
                    await copy2.FetchAttributesAsync();
                    Assert.AreEqual(BlobType.BlockBlob, copy2.BlobType);
                    Assert.AreEqual(blobTier, copy2.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(copy2.Properties.PremiumPageBlobTier.HasValue);

                    Assert.AreEqual(sourceSize * sourceBlockCount, copy.Properties.Length);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }
#endif
        [TestMethod]
        [Description("Test conditional access on a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobConditionalAccess()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CreateForTest(blob, 2, 1024, false);
                blob.FetchAttributes();

                string currentETag = blob.Properties.ETag;
                DateTimeOffset currentModifiedTime = blob.Properties.LastModified.Value;

                // ETag conditional tests
                blob.Metadata["ETagConditionalName"] = "ETagConditionalValue";
                blob.SetMetadata(AccessCondition.GenerateIfMatchCondition(currentETag), null);

                blob.FetchAttributes();
                string newETag = blob.Properties.ETag;
                Assert.AreNotEqual(newETag, currentETag, "ETage should be modified on write metadata");

                blob.Metadata["ETagConditionalName"] = "ETagConditionalValue2";

                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfNoneMatchCondition(newETag), null),
                    "If none match on conditional test should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                string invalidETag = "\"0x10101010\"";
                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfMatchCondition(invalidETag), null),
                    "Invalid ETag on conditional test should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                currentETag = blob.Properties.ETag;
                blob.SetMetadata(AccessCondition.GenerateIfNoneMatchCondition(invalidETag), null);

                blob.FetchAttributes();
                newETag = blob.Properties.ETag;

                // LastModifiedTime tests
                currentModifiedTime = blob.Properties.LastModified.Value;

                blob.Metadata["DateConditionalName"] = "DateConditionalValue";

                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfModifiedSinceCondition(currentModifiedTime), null),
                    "IfModifiedSince conditional on current modified time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                DateTimeOffset pastTime = currentModifiedTime.Subtract(TimeSpan.FromMinutes(5));
                blob.SetMetadata(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null);

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromHours(5));
                blob.SetMetadata(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null);

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromDays(5));
                blob.SetMetadata(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null);

                currentModifiedTime = blob.Properties.LastModified.Value;

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromMinutes(5));
                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null),
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromHours(5));
                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null),
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromDays(5));
                TestHelper.ExpectedException(
                    () => blob.SetMetadata(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null),
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                blob.Metadata["DateConditionalName"] = "DateConditionalValue2";

                currentETag = blob.Properties.ETag;
                blob.SetMetadata(AccessCondition.GenerateIfNotModifiedSinceCondition(currentModifiedTime), null);

                blob.FetchAttributes();
                newETag = blob.Properties.ETag;
                Assert.AreNotEqual(newETag, currentETag, "ETage should be modified on write metadata");
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Put block boundaries")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobPutBlockBoundaries()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                string blockId = GetBlockIdList(1).First();

                byte[] buffer = new byte[0];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    TestHelper.ExpectedException(
                        () => blob.PutBlock(blockId, stream, null),
                        "Trying to upload a block with zero bytes should fail",
                        HttpStatusCode.BadRequest);
                }

                buffer = new byte[1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.PutBlock(blockId, stream, null);
                }

                buffer = new byte[100 * Constants.MB];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.PutBlock(blockId, stream, null);
                }

                buffer = new byte[100 * Constants.MB + 1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    TestHelper.ExpectedException(
                        () => blob.PutBlock(blockId, stream, null),
                        "Trying to upload a block with more than 100MB should fail",
                        HttpStatusCode.RequestEntityTooLarge);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Put block boundaries")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobPutBlockBoundariesAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                string blockId = GetBlockIdList(1).First();

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result;
                    byte[] buffer = new byte[0];
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        result = blob.BeginPutBlock(blockId, stream, null,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        TestHelper.ExpectedException(
                            () => blob.EndPutBlock(result),
                            "Trying to upload a block with zero bytes should fail",
                            HttpStatusCode.BadRequest);
                    }

                    buffer = new byte[1];
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        result = blob.BeginPutBlock(blockId, stream, null,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndPutBlock(result);
                    }

                    buffer = new byte[100 * Constants.MB];
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        result = blob.BeginPutBlock(blockId, stream, null,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndPutBlock(result);
                    }

                    buffer = new byte[100 * Constants.MB + 1];
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        result = blob.BeginPutBlock(blockId, stream, null,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        TestHelper.ExpectedException(
                            () => blob.EndPutBlock(result),
                            "Trying to upload a block with more than 100MB should fail",
                            HttpStatusCode.RequestEntityTooLarge);
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Put block boundaries")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobPutBlockBoundariesTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                string blockId = GetBlockIdList(1).First();

                byte[] buffer = new byte[0];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    TestHelper.ExpectedExceptionTask(
                        blob.PutBlockAsync(blockId, stream, null),
                        "Trying to upload a block with zero bytes should fail",
                        HttpStatusCode.BadRequest);
                }

                buffer = new byte[1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.PutBlockAsync(blockId, stream, null).Wait();
                }

                buffer = new byte[100 * Constants.MB];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    blob.PutBlockAsync(blockId, stream, null).Wait();
                }

                buffer = new byte[100 * Constants.MB + 1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    TestHelper.ExpectedExceptionTask(
                        blob.PutBlockAsync(blockId, stream, null),
                        "Trying to upload a block with more than 100MB should fail",
                        HttpStatusCode.RequestEntityTooLarge);
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload blocks and then verify the contents")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobPutBlockViaCopyAsync()
        {
            byte[] buffer = GetRandomBuffer(4 * 1024 * 1024);

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                BlobContainerPermissions permissions = await container.GetPermissionsAsync();

                permissions.PublicAccess = BlobContainerPublicAccessType.Container;

                await container.SetPermissionsAsync(permissions);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                List<string> blockList = GetBlockIdList(2);

                using (MemoryStream resultingData = new MemoryStream())
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        await blob.PutBlockAsync(blockList[0], memoryStream, null);
                        resultingData.Write(buffer, 0, buffer.Length);

                        int offset = buffer.Length - 1024;
                        memoryStream.Seek(offset, SeekOrigin.Begin);
                        await blob.PutBlockAsync(blockList[1], memoryStream, null);
                        resultingData.Write(buffer, offset, buffer.Length - offset);
                    }

                    await blob.PutBlockListAsync(blockList);

                    CloudBlockBlob destBlob = container.GetBlockBlobReference("blob2");
                    List<string> destBlockList = GetBlockIdList(2);
                    await destBlob.PutBlockAsync(destBlockList[0], blob.Uri, 50, 100, null);
                    await destBlob.PutBlockAsync(destBlockList[1], blob.Uri, 500, 100, null);

                    await destBlob.PutBlockListAsync(destBlockList);

                    using (MemoryStream blobData = new MemoryStream())
                    {
                        await destBlob.DownloadToStreamAsync(blobData);
                        Assert.AreEqual(200, blobData.Length);

                        byte[] expectedData = resultingData.ToArray();
                        expectedData =
                            expectedData
                            .Skip(50).Take(100)
                            .Concat(expectedData.Skip(500).Take(100))
                            .ToArray();

                        Assert.IsTrue(blobData.ToArray().SequenceEqual(expectedData), "downloaded data doesn't match expected data");
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Upload blocks and then verify the contents")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobPutBlock()
        {
            byte[] buffer = GetRandomBuffer(4 * Constants.MB);
            MD5 md5 = MD5.Create();
            string contentMD5 = Convert.ToBase64String(md5.ComputeHash(buffer));

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                List<string> blockList = GetBlockIdList(2);

                using (MemoryStream resultingData = new MemoryStream())
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        blob.PutBlock(blockList[0], memoryStream, contentMD5);
                        resultingData.Write(buffer, 0, buffer.Length);

                        int offset = buffer.Length - 1024;
                        memoryStream.Seek(offset, SeekOrigin.Begin);
                        TestHelper.ExpectedException(
                            () => blob.PutBlock(blockList[1], memoryStream, contentMD5),
                            "Invalid MD5 should fail with mismatch",
                            HttpStatusCode.BadRequest,
                            "Md5Mismatch");

                        memoryStream.Seek(offset, SeekOrigin.Begin);
                        blob.PutBlock(blockList[1], memoryStream, null);
                        resultingData.Write(buffer, offset, buffer.Length - offset);
                    }

                    blob.PutBlockList(blockList);

                    using (MemoryStream blobData = new MemoryStream())
                    {
                        blob.DownloadToStream(blobData);
                        Assert.AreEqual(resultingData.Length, blobData.Length);

                        Assert.IsTrue(blobData.ToArray().SequenceEqual(resultingData.ToArray()));
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Upload blocks and then verify the contents")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobPutBlockAPM()
        {
            byte[] buffer = GetRandomBuffer(4 * Constants.MB);
            MD5 md5 = MD5.Create();
            string contentMD5 = Convert.ToBase64String(md5.ComputeHash(buffer));

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                List<string> blockList = GetBlockIdList(2);

                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result;

                    using (MemoryStream resultingData = new MemoryStream())
                    {
                        using (MemoryStream memoryStream = new MemoryStream(buffer))
                        {
                            memoryStream.Seek(0, SeekOrigin.Begin);
                            result = blob.BeginPutBlock(blockList[0], memoryStream, contentMD5,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndPutBlock(result);
                            resultingData.Write(buffer, 0, buffer.Length);

                            int offset = buffer.Length - 1024;
                            memoryStream.Seek(offset, SeekOrigin.Begin);
                            result = blob.BeginPutBlock(blockList[1], memoryStream, contentMD5,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            TestHelper.ExpectedException(
                                () => blob.EndPutBlock(result),
                                "Invalid MD5 should fail with mismatch",
                                HttpStatusCode.BadRequest,
                                "Md5Mismatch");

                            memoryStream.Seek(offset, SeekOrigin.Begin);
                            result = blob.BeginPutBlock(blockList[1], memoryStream, null,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            blob.EndPutBlock(result);
                            resultingData.Write(buffer, offset, buffer.Length - offset);
                        }

                        blob.PutBlockList(blockList);

                        using (MemoryStream blobData = new MemoryStream())
                        {
                            blob.DownloadToStream(blobData);
                            Assert.AreEqual(resultingData.Length, blobData.Length);

                            Assert.IsTrue(blobData.ToArray().SequenceEqual(resultingData.ToArray()));
                        }
                    }
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Test block blob methods on a page blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobMethodsOnPageBlob()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                List<string> blobs = await CreateBlobs(container, 1, BlobType.PageBlob);
                CloudBlockBlob blob = container.GetBlockBlobReference(blobs.First());
                List<string> blockList = GetBlockIdList(1);

                byte[] buffer = new byte[1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    TestHelper.ExpectedException(
                        () => blob.PutBlock(blockList.First(), stream, null),
                        "Block operations should fail on page blobs",
                        HttpStatusCode.Conflict,
                        "InvalidBlobType");
                }

                TestHelper.ExpectedException(
                    () => blob.PutBlockList(blockList),
                    "Block operations should fail on page blobs",
                    HttpStatusCode.Conflict,
                    "InvalidBlobType");

                TestHelper.ExpectedException(
                    () => blob.DownloadBlockList(),
                    "Block operations should fail on page blobs",
                    HttpStatusCode.Conflict,
                    "InvalidBlobType");
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Test block removal/addition/reordering in a block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobBlockReordering()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                List<string> originalBlockIds = GetBlockIdList(10);
                List<string> blockIds = new List<string>(originalBlockIds);
                List<byte[]> blocks = new List<byte[]>();
                for (int i = 0; i < blockIds.Count; i++)
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(i.ToString());
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        blob.PutBlock(blockIds[i], stream, null);
                    }
                    blocks.Add(buffer);
                }
                blob.PutBlockList(blockIds);
                Assert.AreEqual("0123456789", DownloadText(blob, Encoding.UTF8));

                blockIds.RemoveAt(0);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("123456789", DownloadText(blob, Encoding.UTF8));

                blockIds.RemoveAt(8);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("12345678", DownloadText(blob, Encoding.UTF8));

                blockIds.RemoveAt(3);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("1235678", DownloadText(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[9]))
                {
                    blob.PutBlock(originalBlockIds[9], stream, null);
                }
                blockIds.Insert(0, originalBlockIds[9]);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("91235678", DownloadText(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[0]))
                {
                    blob.PutBlock(originalBlockIds[0], stream, null);
                }
                blockIds.Add(originalBlockIds[0]);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("912356780", DownloadText(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[4]))
                {
                    blob.PutBlock(originalBlockIds[4], stream, null);
                }
                blockIds.Insert(2, originalBlockIds[4]);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("9142356780", DownloadText(blob, Encoding.UTF8));

                blockIds.Insert(0, originalBlockIds[0]);
                blob.PutBlockList(blockIds);
                Assert.AreEqual("09142356780", DownloadText(blob, Encoding.UTF8));
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Upload and download null/empty data")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadDownloadNoData()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob");
                TestHelper.ExpectedException<ArgumentNullException>(
                    () => blob.UploadFromStream(null),
                    "Uploading from a null stream should fail");

                using (MemoryStream stream = new MemoryStream())
                {
                    blob.UploadFromStream(stream);
                }

                TestHelper.ExpectedException<ArgumentNullException>(
                    () => blob.DownloadToStream(null),
                    "Downloading to a null stream should fail");

                using (MemoryStream stream = new MemoryStream())
                {
                    blob.DownloadToStream(stream);
                    Assert.AreEqual(0, stream.Length);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("List committed and uncommitted blobs")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobListUncommittedBlobs()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                List<string> committedBlobs = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    string name = "cblob" + i.ToString();
                    CloudBlockBlob committedBlob = container.GetBlockBlobReference(name);
                    CreateForTest(committedBlob, 2, 1024, false);
                    committedBlobs.Add(name);
                }

                List<string> uncommittedBlobs = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string name = "ucblob" + i.ToString();
                    CloudBlockBlob uncommittedBlob = container.GetBlockBlobReference(name);
                    CreateForTest(uncommittedBlob, 2, 1024, false, false);
                    uncommittedBlobs.Add(name);
                }

                List<IListBlobItem> blobs = container.ListBlobs(null, true, BlobListingDetails.UncommittedBlobs).ToList();
                foreach (CloudBlob blob in blobs)
                {
                    if (committedBlobs.Remove(blob.Name))
                    {
                        Assert.AreEqual(2 * 1024, blob.Properties.Length);
                    }
                    else if (uncommittedBlobs.Remove(blob.Name))
                    {
                        Assert.AreEqual(0, blob.Properties.Length);
                    }
                    else
                    {
                        Assert.Fail("Blob is not found in either committed or uncommitted list");
                    }
                }

                Assert.AreEqual(0, committedBlobs.Count);
                Assert.AreEqual(0, uncommittedBlobs.Count);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Try operations with an invalid Sas and snapshot")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobInvalidSasAndSnapshot()
        {
            // Sas token creds.
            string token = "?sp=abcde&sig=1";
            StorageCredentials creds = new StorageCredentials(token);
            Assert.IsTrue(creds.IsSAS);

            // Client with shared key access.
            CloudBlobClient blobClient = GenerateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(GetRandomContainerName());
            try
            {
                container.Create();

                SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write,
                };
                string sasToken = container.GetSharedAccessSignature(policy);

                string blobUri = container.Uri.AbsoluteUri + "/blob1" + sasToken;
                TestHelper.ExpectedException<ArgumentException>(
                    () => new CloudBlockBlob(new Uri(blobUri), container.ServiceClient.Credentials),
                    "Try to use SAS creds in Uri on a shared key client");

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.UploadFromStream(new MemoryStream(GetRandomBuffer(10)));

                CloudBlockBlob snapshot = blob.CreateSnapshot();
                DateTimeOffset? wrongTime = snapshot.SnapshotTime.Value + TimeSpan.FromSeconds(10);

                string snapshotUri = snapshot.Uri + "?snapshot=" + wrongTime.Value.ToString();
                TestHelper.ExpectedException<ArgumentException>(
                    () => new CloudBlockBlob(new Uri(snapshotUri), snapshot.SnapshotTime, container.ServiceClient.Credentials),
                    "Snapshot in Uri does not match snapshot on blob");

            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Upload and download text")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadText()
        {
            this.DoTextUploadDownload("test", false, false);
            this.DoTextUploadDownload("char中文test", true, false);
            this.DoTextUploadDownload("", false, false);
        }

        [TestMethod]
        [Description("Upload and download text")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadTextAPM()
        {
            this.DoTextUploadDownload("test", false, true);
            this.DoTextUploadDownload("char中文test", true, true);
            this.DoTextUploadDownload("", false, true);
        }

        [TestMethod]
        [Description("Generate SAS for Snapshots")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobGenerateSASForSnapshot()
        {
            // Client with shared key access.
            CloudBlobClient blobClient = GenerateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(GetRandomContainerName());
            MemoryStream memoryStream = new MemoryStream();
            try
            {
                container.Create();
                CloudBlockBlob blob = container.GetBlockBlobReference("Testing");
                blob.UploadFromStream(new MemoryStream(GetRandomBuffer(10)));
                SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write,
                };

                CloudBlockBlob snapshot = blob.CreateSnapshot();
                string sas = snapshot.GetSharedAccessSignature(policy);
                Assert.IsNotNull(sas);
                StorageCredentials credentials = new StorageCredentials(sas);
                Uri snapshotUri = snapshot.SnapshotQualifiedUri;
                CloudBlockBlob blob1 = new CloudBlockBlob(snapshotUri, credentials);
                blob1.DownloadToStream(memoryStream);
                Assert.IsNotNull(memoryStream);
            }
            finally
            {
                container.DeleteIfExists();
                memoryStream.Close();
            }
        }

#if TASK
        [TestMethod]
        [Description("Upload and download text")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadTextTask()
        {
            this.DoTextUploadDownloadTask("test", false);
            this.DoTextUploadDownloadTask("char中文test", true);
            this.DoTextUploadDownloadTask("", false);
        }
#endif

        [TestMethod]
        [Description("Test for ensuring read failures are tolerated within Upload/OpenWrite operations with write-only Account SAS permissions.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadTestUploadWithWriteOnlyAccountSAS()
        {
            string blobName = "n" + Guid.NewGuid().ToString("N");
            CloudBlobContainer containerWithSAS = GenerateRandomWriteOnlyBlobContainer();
            containerWithSAS.CreateIfNotExists();
            CloudBlockBlob blockBlobWithSAS = containerWithSAS.GetBlockBlobReference(blobName);
            int bufferSize = (int)(24 * Constants.MB);
            byte[] buffer = GetRandomBuffer(bufferSize);
            MemoryStream streamBuffer = new MemoryStream(buffer);

            try
            {
                blockBlobWithSAS.UploadText("Initializing Block Blob");
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfNotExistsCondition()),
                    "Should fail as specified blob already exists.",
                    HttpStatusCode.Conflict);
                blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfExistsCondition());

                blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfNoneMatchCondition("\"etag\""));
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfMatchCondition("\"etag\"")),
                    "Should fail as Match Condition is not met.",
                    HttpStatusCode.PreconditionFailed);

                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfNotExistsCondition()),
                    "Should fail as specified blob already exists.",
                    HttpStatusCode.Conflict);
                blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfExistsCondition());

                blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfNoneMatchCondition("\"abcd\""));
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfMatchCondition("\"abcd\"")),
                    "Should fail as Match Condition is not met.",
                HttpStatusCode.PreconditionFailed);
            }
            finally
            {
                containerWithSAS.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Test to verify the correct functionality of OpenWrite with write-only Account SAS permissions.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobUploadTestOpenWriteWithWriteOnlyAccountSAS()
        {
            CloudBlobContainer containerWithSAS = GenerateRandomWriteOnlyBlobContainer();
            containerWithSAS.CreateIfNotExists();

             try 
             {
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    CloudBlockBlob existingBlob = containerWithSAS.GetBlockBlobReference("blob");
                    existingBlob.PutBlockList(new List<string>());
                    CloudBlockBlob blob = containerWithSAS.GetBlockBlobReference("blob2");
                   
                    //Should normally fail with Read Permissions
                    AccessCondition accessCondition = AccessCondition.GenerateIfMatchCondition(existingBlob.Properties.ETag);
                    IAsyncResult result = blob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blob.EndOpenWrite(result);
                 
                    blob = containerWithSAS.GetBlockBlobReference("blob3");
                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition(existingBlob.Properties.ETag);
                    result = blob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    Stream blobStream = blob.EndOpenWrite(result);
                    blobStream.Dispose();

                    blob = containerWithSAS.GetBlockBlobReference("blob4");
                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition("*");
                    result = blob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = blob.EndOpenWrite(result);
                    blobStream.Dispose();

                    blob = containerWithSAS.GetBlockBlobReference("blob5");
                    accessCondition = AccessCondition.GenerateIfModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(1));
                    result = blob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = blob.EndOpenWrite(result);
                    blobStream.Dispose();

                    blob = containerWithSAS.GetBlockBlobReference("blob6");
                    accessCondition = AccessCondition.GenerateIfNotModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(-1));
                    result = blob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = blob.EndOpenWrite(result);
                    blobStream.Dispose();

                    accessCondition = AccessCondition.GenerateIfMatchCondition(existingBlob.Properties.ETag);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    blobStream.Dispose();

                    //Should normally fail with Read Permissions.
                    accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    existingBlob.EndOpenWrite(result);

                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    blobStream.Dispose();

                    //Should normally fail with read permissions
                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition(existingBlob.Properties.ETag);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    existingBlob.EndOpenWrite(result);
                   
                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition("*");
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    TestHelper.ExpectedException(
                        () => blobStream.Dispose(),
                        "BlobWriteStream.Dispose with a non-met condition should fail",
                        HttpStatusCode.Conflict);

                    accessCondition = AccessCondition.GenerateIfModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(-1));
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    blobStream.Dispose();

                    //Should normally fail with read permissions
                    accessCondition = AccessCondition.GenerateIfModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(1));
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    existingBlob.EndOpenWrite(result);
                   

                    accessCondition = AccessCondition.GenerateIfNotModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(1));
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    blobStream.Dispose();

                    //Should normally fail with read permissions
                    accessCondition = AccessCondition.GenerateIfNotModifiedSinceCondition(existingBlob.Properties.LastModified.Value.AddMinutes(-1));
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    existingBlob.EndOpenWrite(result);

                    accessCondition = AccessCondition.GenerateIfMatchCondition(existingBlob.Properties.ETag);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    existingBlob.SetProperties();
                    TestHelper.ExpectedException(
                        () => blobStream.Dispose(),
                        "BlobWriteStream.Dispose with a non-met condition should fail",
                        HttpStatusCode.PreconditionFailed);

                    blob = containerWithSAS.GetBlockBlobReference("blob7");
                    accessCondition = AccessCondition.GenerateIfNoneMatchCondition("*");
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);
                    blob.PutBlockList(new List<string>());
                    TestHelper.ExpectedException(
                        () => blobStream.Dispose(),
                        "BlobWriteStream.Dispose with a non-met condition should fail",
                        HttpStatusCode.Conflict);

                    blob = containerWithSAS.GetBlockBlobReference("blob8");
                    accessCondition = AccessCondition.GenerateIfNotModifiedSinceCondition(existingBlob.Properties.LastModified.Value);
                    result = existingBlob.BeginOpenWrite(accessCondition, null, null,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    blobStream = existingBlob.EndOpenWrite(result);

                    // Wait 1 second so that the last modified time of the blob is in the past
                    Thread.Sleep(TimeSpan.FromSeconds(1));

                    existingBlob.SetProperties();
                    TestHelper.ExpectedException(
                        () => blobStream.Dispose(),
                        "BlobWriteStream.Dispose with a non-met condition should fail",
                        HttpStatusCode.PreconditionFailed);
                }
            }
            finally
            {
                containerWithSAS.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Test for ensuring read failures are tolerated within Upload/OpenWrite operations with write-only Service SAS permissions.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockUplodadTestUploadWithWriteOnlyServiceSAS()
        {
            string containerName = "c" + Guid.NewGuid().ToString("N");
            string blobName = "n" + Guid.NewGuid().ToString("N");

            SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                Permissions = SharedAccessBlobPermissions.Delete | SharedAccessBlobPermissions.Write,
            };

            CloudBlobClient blobClient = GenerateCloudBlobClient();
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(containerName);
            blobContainer.CreateIfNotExists();
            CloudBlockBlob blockBlob = blobContainer.GetBlockBlobReference(blobName);
            blockBlob.UploadText("Initializing Block Blob");
            string sasBlobToken = blockBlob.GetSharedAccessSignature(policy);
            string blockBlobSAS = blockBlob.Uri + sasBlobToken;
            CloudBlockBlob blockBlobWithSAS = new CloudBlockBlob(new Uri(blockBlobSAS));

            int bufferSize = (int)(24 * Constants.MB);
            byte[] buffer = GetRandomBuffer(bufferSize);
            MemoryStream streamBuffer = new MemoryStream(buffer);

            try
            {
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfNotExistsCondition()),
                    "Should fail as specified blob already exists.",
                    HttpStatusCode.Conflict);
                blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfExistsCondition());

                blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfNoneMatchCondition("\"etag\""));
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromByteArray(buffer, 0, bufferSize, AccessCondition.GenerateIfMatchCondition("\"etag\"")),
                    "Should fail as Match Condition is not met.",
                    HttpStatusCode.PreconditionFailed);

                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfNotExistsCondition()),
                    "Should fail as specified blob already exists.",
                    HttpStatusCode.Conflict);
                blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfExistsCondition());

                blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfNoneMatchCondition("\"etag\""));
                TestHelper.ExpectedException(
                    () => blockBlobWithSAS.UploadFromStream(streamBuffer, AccessCondition.GenerateIfMatchCondition("\"etag\"")),
                    "Should fail as Match Condition is not met.",
                    HttpStatusCode.PreconditionFailed);
            }
            finally
            {
                blobContainer.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Tests if exceptions are propagated up from UploadFromMultiStreamAsync")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobTestExceptionUploadFromMultiStreamAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            container.Create();
            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("test1");
                List<Stream> uploadList = new List<Stream>();
                OperationContext operationContext = new OperationContext();
                long blockSize = 4 * Constants.MB + 1;
                int blockNum = 0;
                Object thisLock = new Object();
                BlobRequestOptions options = new BlobRequestOptions()
                {
                    StoreBlobContentMD5 = false,
                    ParallelOperationThreadCount = 2,
                };

                operationContext.SendingRequest += (sender, e) =>
                {
                    lock (thisLock)
                    {
                        if (++blockNum >= 3)
                        {
                            HttpRequestHandler.SetContentLength(e.Request, 32);
                        }
                    }
                };

                for (int i = 0; i < 6; i++)
                {
                    uploadList.Add(new MemoryStream(GetRandomBuffer(blockSize)));
                }

                CancellationTokenSource tokenSource = new CancellationTokenSource(30000);
                Task blockUpload = blob.UploadFromMultiStreamAsync(uploadList, null, options, operationContext, null /*progressHandler*/, CancellationToken.None);
                TestHelper.ExpectedExceptionTask(blockUpload, "UploadFromMultiStream", 0);

                uploadList.Clear();
                blockNum = 0;
                operationContext.SendingRequest += (sender, e) =>
                {
                    lock (thisLock)
                    {
                        if (++blockNum >= 3)
                        {
                            HttpRequestHandler.SetContentLength(e.Request, blockSize * 2);
                        }
                    }
                };

                for (int i = 0; i < 6; i++)
                {
                    uploadList.Add(new MemoryStream(GetRandomBuffer(blockSize)));
                }

                blockUpload = blob.UploadFromMultiStreamAsync(uploadList, null, options, operationContext, null /*progressHandler*/, CancellationToken.None);

#if NETCOREAPP2_0
                TestHelper.ExpectedExceptionTask(blockUpload, "UploadFromMultiStream", HttpStatusCode.RequestTimeout);
#else
                TestHelper.ExpectedExceptionTask(blockUpload, "UploadFromMultiStream", 0);
#endif

                blob.StreamWriteSizeInBytes = (int)(4 * Constants.MB + 1);
                using (MemoryStream ms = new MemoryStream(GetRandomBuffer(20 * 1024 * 1024)))
                {
                    blockNum = 0;
                    TestHelper.ExpectedException<StorageException>(() => blob.UploadFromStream(ms, null, options, operationContext), "UploadfromStream", "abc");
                    ms.Seek(0, SeekOrigin.Begin);

                    blockNum = 0;
                    Task uploadTask = blob.UploadFromStreamAsync(ms, null, options, operationContext);
                    TestHelper.ExpectedExceptionTask<StorageException>(uploadTask, "UploadFromStreamAsync");
                    ms.Seek(0, SeekOrigin.Begin);

                    await blob.UploadFromStreamAsync(ms, null, options, null);
                }
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        private void DoTextUploadDownload(string text, bool checkDifferentEncoding, bool isAsync)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateIfNotExists();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                if (isAsync)
                {
                    IAsyncResult result;
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        result = blob.BeginUploadText(text,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndUploadText(result);
                        result = blob.BeginDownloadText(
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        Assert.AreEqual(text, blob.EndDownloadText(result));
                        if (checkDifferentEncoding)
                        {
                            result = blob.BeginDownloadText(Encoding.Unicode, null, null, null,
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            Assert.AreNotEqual(text, blob.EndDownloadText(result));
                        }

                        OperationContext context = new OperationContext();
                        result = blob.BeginUploadText(text, Encoding.Unicode, null, null, context,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        blob.EndUploadText(result);
                        Assert.AreEqual(1, context.RequestResults.Count);
                        result = blob.BeginDownloadText(Encoding.Unicode, null, null, context,
                            ar => waitHandle.Set(),
                            null);
                        waitHandle.WaitOne();
                        Assert.AreEqual(text, blob.EndDownloadText(result));
                        Assert.AreEqual(2, context.RequestResults.Count);
                        if (checkDifferentEncoding)
                        {
                            result = blob.BeginDownloadText(
                                ar => waitHandle.Set(),
                                null);
                            waitHandle.WaitOne();
                            Assert.AreNotEqual(text, blob.EndDownloadText(result));
                        }
                    }
                }
                else
                {
                    blob.UploadText(text);
                    Assert.AreEqual(text, blob.DownloadText());
                    if (checkDifferentEncoding)
                    {
                        Assert.AreNotEqual(text, blob.DownloadText(Encoding.Unicode));
                    }

                    blob.UploadText(text, Encoding.Unicode);
                    Assert.AreEqual(text, blob.DownloadText(Encoding.Unicode));
                    if (checkDifferentEncoding)
                    {
                        Assert.AreNotEqual(text, blob.DownloadText());
                    }

                    OperationContext context = new OperationContext();
                    blob.UploadText(text, Encoding.Unicode, null, null, context);
                    Assert.AreEqual(1, context.RequestResults.Count);
                    blob.DownloadText(Encoding.Unicode, null, null, context);
                    Assert.AreEqual(2, context.RequestResults.Count);
                }
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        private void DoTextUploadDownloadTask(string text, bool checkDifferentEncoding)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateIfNotExistsAsync().Wait();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                blob.UploadTextAsync(text).Wait();
                Assert.AreEqual(text, blob.DownloadTextAsync().Result);
                if (checkDifferentEncoding)
                {
                    Assert.AreNotEqual(text, blob.DownloadTextAsync(Encoding.Unicode, null, null, null).Result);
                }

                blob.UploadTextAsync(text, Encoding.Unicode, null, null, null).Wait();
                Assert.AreEqual(text, blob.DownloadTextAsync(Encoding.Unicode, null, null, null).Result);
                if (checkDifferentEncoding)
                {
                    Assert.AreNotEqual(text, blob.DownloadTextAsync().Result);
                }

                OperationContext context = new OperationContext();
                blob.UploadTextAsync(text, Encoding.Unicode, null, null, context).Wait();
                Assert.AreEqual(1, context.RequestResults.Count);
                blob.DownloadTextAsync(Encoding.Unicode, null, null, context).Wait();
                Assert.AreEqual(2, context.RequestResults.Count);
            }
            finally
            {
                container.DeleteIfExists();
            }
        }
#endif

        [TestMethod]
        [Description("Test server failure retry case.")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobFailoverRetry()
        {
            OperationContext opContext = new OperationContext();
            CloudBlobClient badPrimaryClient = new CloudBlobClient(new StorageUri(new Uri("http://1.2.3.4/x//"), new Uri(TargetTenantConfig.BlobServiceSecondaryEndpoint)), TestBase.StorageCredentials);
            CloudBlockBlob secondaryBlob = badPrimaryClient.GetContainerReference(GetRandomContainerName()).GetBlockBlobReference("testblob");
            badPrimaryClient.DefaultRequestOptions.RetryPolicy = new RetryPolicies.LinearRetry(TimeSpan.Zero, 1);
            badPrimaryClient.DefaultRequestOptions.LocationMode = RetryPolicies.LocationMode.PrimaryThenSecondary;
            try
            {
                secondaryBlob.DownloadText(operationContext: opContext);
            }
            catch (StorageException)
            {
                Assert.IsTrue(opContext.RequestResults.Count > 1);
                Assert.AreEqual(StorageLocation.Secondary, opContext.LastResult.TargetLocation);
            }
        }

        [TestMethod]
        [Description("GetAccountProperties via Block Blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobGetAccountProperties()
        {
            CloudBlobContainer blobContainerWithSAS = GenerateRandomWriteOnlyBlobContainer();
            try
            {
                blobContainerWithSAS.Create();

                CloudBlockBlob blob = blobContainerWithSAS.GetBlockBlobReference("test");

                AccountProperties result = blob.GetAccountPropertiesAsync().Result;

                blob.DeleteIfExists();

                Assert.IsNotNull(result);

                Assert.IsNotNull(result.SkuName);

                Assert.IsNotNull(result.AccountKind);
            }
            finally
            {
                blobContainerWithSAS.DeleteIfExists();
            }
        }
    }
}
