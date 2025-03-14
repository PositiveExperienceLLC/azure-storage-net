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
using Microsoft.Azure.Storage.Blob.Protocol;
using Microsoft.Azure.Storage.Shared.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Core.Util;
using System.Threading;

#if NETCORE
using System.Security.Cryptography;
#else
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
#endif

namespace Microsoft.Azure.Storage.Blob
{
    [TestClass]
    public class CloudBlockBlobTest : BlobTestBase
    {
        internal static async Task CreateForTestAsync(CloudBlockBlob blob, int blockCount, int blockSize, bool commit = true)
        {
            byte[] buffer = GetRandomBuffer(blockSize);
            List<string> blocks = GetBlockIdList(blockCount);

            foreach (string block in blocks)
            {
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await blob.PutBlockAsync(block, stream, null);
                }
            }

            if (commit)
            {
                await blob.PutBlockListAsync(blocks, null, null, null);
            }

            await Task.Delay(1000);
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

                await CreateForTestAsync(blob1, 1, 1);

                // Test a batch with one successful operation.
                BlobSetTierBatchOperation batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob1, StandardBlobTier.Cool, null, null);

                var results = await blob1.ServiceClient.ExecuteBatchAsync(batch);

                Assert.AreEqual(results.Count, 1);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.OK);

                // Test a batch with multiple successful operations.
                // TODO: Test with page blob when it is possible to get a premium account with batch enabled.
                await CreateForTestAsync(blob2, 1, 1);

                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob1, StandardBlobTier.Hot);
                batch.AddSubOperation(blob2, StandardBlobTier.Cool);

                results = await blob1.ServiceClient.ExecuteBatchAsync(batch);

                Assert.AreEqual(results.Count, 2);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.OK);

                // Test a batch with one failure.

                batch = new BlobSetTierBatchOperation();
                batch.AddSubOperation(blob3, StandardBlobTier.Hot);

                try
                {
                    await blob1.ServiceClient.ExecuteBatchAsync(batch);
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
                    await blob1.ServiceClient.ExecuteBatchAsync(batch);
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
                    await blob1.ServiceClient.ExecuteBatchAsync(batch);
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
                    await blob1.ServiceClient.ExecuteBatchAsync(batch);
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
                await CreateForTestAsync(blob4, 1, 1);
                await CreateForTestAsync(blob5, 1, 1);

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

                await CreateForTestAsync(blob1, 1, 1);
                await CreateForTestAsync(blob2, 1, 1);

                var batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob1);
                batch.AddSubOperation(blob2);

                var results = await blob1.ServiceClient.ExecuteBatchAsync(batch);

                Assert.AreEqual(2, results.Count);
                ValidateSuccessfulBatchResponse(results, HttpStatusCode.Accepted);

                // Test a batch with one failure.
                await CreateForTestAsync(blob1, 1, 1);
                await CreateForTestAsync(blob2, 1, 1);

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
                await CreateForTestAsync(blob1, 1, 1);
                await CreateForTestAsync(blob2, 1, 1);

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
                var blob7Name = "blob7";
                var blob8Name = "blob8";
                var blob7 = container.GetBlockBlobReference(blob7Name);
                var blob8 = container.GetBlockBlobReference(blob8Name);
                await CreateForTestAsync(blob7, 1, 1);
                await CreateForTestAsync(blob8, 1, 1);

                var policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddDays(1),
                    Permissions = SharedAccessBlobPermissions.Delete
                };

                string sas = container.GetSharedAccessSignature(policy);
                var sasContainer = new CloudBlobContainer(new Uri(container.Uri.ToString() + sas));
                blob7 = sasContainer.GetBlockBlobReference(blob7Name);
                blob8 = sasContainer.GetBlockBlobReference(blob8Name);

                batch = new BlobDeleteBatchOperation();
                batch.AddSubOperation(blob7);
                batch.AddSubOperation(blob8);

                await blob1.ServiceClient.ExecuteBatchAsync(batch).ConfigureAwait(false);
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
        public async Task CloudBlockBlobCreateAndDeleteAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 0, 0);
                Assert.IsTrue(await blob.ExistsAsync());
                await blob.DeleteAsync();
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Try to delete a non-existing block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobDeleteIfExistsAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                Assert.IsFalse(await blob.DeleteIfExistsAsync());
                await CreateForTestAsync(blob, 0, 0);
                Assert.IsTrue(await blob.DeleteIfExistsAsync());
                Assert.IsFalse(await blob.DeleteIfExistsAsync());
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Check a blob's existence")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobExistsAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();

            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");

                Assert.IsFalse(await blob2.ExistsAsync());

                await CreateForTestAsync(blob, 2, 1024);

                Assert.IsTrue(await blob2.ExistsAsync());
                Assert.AreEqual(2048, blob2.Properties.Length);

                await blob.DeleteAsync();

                Assert.IsFalse(await blob2.ExistsAsync());
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify the attributes of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobFetchAttributesAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);
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
                await blob2.FetchAttributesAsync();
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

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob3.Properties.ContentMD5);
                byte[] target = new byte[4];
                BlobRequestOptions options2 = new BlobRequestOptions();
                options2.UseTransactionalMD5 = true;
                blob3.Properties.ContentMD5 = "MDAwMDAwMDA=";
                await blob3.DownloadRangeToByteArrayAsync(target, 0, 0, 4, null, options2, null);
                Assert.IsNull(blob3.Properties.ContentMD5);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify setting the properties of a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobSetPropertiesAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);
                string eTag = blob.Properties.ETag;
                DateTimeOffset lastModified = blob.Properties.LastModified.Value;

                await Task.Delay(1000);

                blob.Properties.CacheControl = "no-transform";
                blob.Properties.ContentDisposition = "attachment";
                blob.Properties.ContentEncoding = "gzip";
                blob.Properties.ContentLanguage = "tr,en";
                blob.Properties.ContentMD5 = "MDAwMDAwMDA=";
                blob.Properties.ContentType = "text/html";
                await blob.SetPropertiesAsync();
                Assert.IsTrue(blob.Properties.LastModified > lastModified);
                Assert.AreNotEqual(eTag, blob.Properties.ETag);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                await blob2.FetchAttributesAsync();
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
                    await blob3.DownloadToStreamAsync(stream, null, options, null);
                }
                AssertAreEqual(blob2.Properties, blob3.Properties);

                BlobResultSegment results = await container.ListBlobsSegmentedAsync(null);
                CloudBlockBlob blob4 = (CloudBlockBlob)results.Results.First();
                AssertAreEqual(blob2.Properties, blob4.Properties);

                CloudBlockBlob blob5 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob5.Properties.ContentMD5);
                byte[] target = new byte[4];
                await blob5.DownloadRangeToByteArrayAsync(target, 0, 0, 4);
                Assert.AreEqual("MDAwMDAwMDA=", blob5.Properties.ContentMD5);

                CloudBlockBlob blob6 = container.GetBlockBlobReference("blob1");
                Assert.IsNull(blob6.Properties.ContentMD5);
                target = new byte[4];
                BlobRequestOptions options2 = new BlobRequestOptions();
                options2.UseTransactionalMD5 = true;
                await blob6.DownloadRangeToByteArrayAsync(target, 0, 0, 4, null, options2, null);
                Assert.AreEqual("MDAwMDAwMDA=", blob6.Properties.ContentMD5);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#if NETCORE
        [TestMethod]
        [Description("Verify setting the properties of a blob with spacial characters such as '<' and getting them")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlobSetPropertiesSpecialCharactersAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);
                string eTag = blob.Properties.ETag;
                DateTimeOffset lastModified = blob.Properties.LastModified.Value;

                await Task.Delay(1000);

                blob.Properties.CacheControl = "no-trans>form";
                blob.Properties.ContentDisposition = "a<ttachment";
                blob.Properties.ContentEncoding = "gzi<p";
                blob.Properties.ContentLanguage = "tr,en>";
                blob.Properties.ContentMD5 = "MDAwMDAwMDA=";
                blob.Properties.ContentType = "text</html";
                await blob.SetPropertiesAsync();
                Assert.IsTrue(blob.Properties.LastModified > lastModified);
                Assert.AreNotEqual(eTag, blob.Properties.ETag);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                await blob2.FetchAttributesAsync();
                Assert.AreEqual("no-trans>form", blob2.Properties.CacheControl);
                Assert.AreEqual("a<ttachment", blob2.Properties.ContentDisposition);
                Assert.AreEqual("gzi<p", blob2.Properties.ContentEncoding);
                Assert.AreEqual("tr,en>", blob2.Properties.ContentLanguage);
                Assert.AreEqual("MDAwMDAwMDA=", blob2.Properties.ContentMD5);
                Assert.AreEqual("text</html", blob2.Properties.ContentType);
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
        public async Task CloudBlockBlobFetchAttributesInvalidTypeAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);

                CloudPageBlob blob2 = container.GetPageBlobReference("blob1");
                OperationContext operationContext = new OperationContext();

                Assert.ThrowsException<AggregateException>(
                    () => blob2.FetchAttributesAsync(null, null, operationContext).Wait(),
                    "Fetching attributes of a block blob using a page blob reference should fail");
                Assert.IsInstanceOfType(operationContext.LastResult.Exception.InnerException, typeof(InvalidOperationException));
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify additional user-defined query parameters do not disrupt a normal request")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobFetchAttributesSpecialQueryParametersAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                // Ensure that unkown query parameters set by the user are signed properly but ignored, allowing the operation to succeed
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);
                UriBuilder blobURIBuilder = new UriBuilder(blob.Uri);
                blobURIBuilder.Query = "MyQuery=value&YOURQUERY=value2";
                blob = new CloudBlockBlob(blobURIBuilder.Uri, blob.ServiceClient.Credentials);

                await blob.FetchAttributesAsync();
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
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify that creating a block blob can also set its metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobCreateWithMetadataAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                string md5 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });

                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.Metadata["key1"] = "value1";
                blob.Properties.CacheControl = "no-transform";
                blob.Properties.ContentDisposition = "attachment";
                blob.Properties.ContentEncoding = "gzip";
                blob.Properties.ContentLanguage = "tr,en";
                blob.Properties.ContentMD5 = md5;
                blob.Properties.ContentType = "text/html";
                await CreateForTestAsync(blob, 0, 0);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                await blob2.FetchAttributesAsync();
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
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify that a block blob's metadata can be updated")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobSetMetadataAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 0, 0);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                await blob2.FetchAttributesAsync();
                Assert.AreEqual(0, blob2.Metadata.Count);

                OperationContext operationContext = new OperationContext();
                blob.Metadata["key1"] = null;

                Assert.ThrowsException<AggregateException>(
                    () => blob.SetMetadataAsync(null, null, operationContext).Wait(),
                    "Metadata keys should have a non-null value");
                Assert.IsInstanceOfType(operationContext.LastResult.Exception.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "";
                Assert.ThrowsException<AggregateException>(
                    () => blob.SetMetadataAsync(null, null, operationContext).Wait(),
                    "Metadata keys should have a non-empty value");
                Assert.IsInstanceOfType(operationContext.LastResult.Exception.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = " ";
                Assert.ThrowsException<AggregateException>(
                    () => blob.SetMetadataAsync(null, null, operationContext).Wait(),
                    "Metadata keys should have a non-whitespace only value");
                Assert.IsInstanceOfType(operationContext.LastResult.Exception.InnerException, typeof(ArgumentException));

                blob.Metadata["key1"] = "value1";
                await blob.SetMetadataAsync();

                await blob2.FetchAttributesAsync();
                Assert.AreEqual(1, blob2.Metadata.Count);
                Assert.AreEqual("value1", blob2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1", blob2.Metadata["KEY1"]);

                BlobResultSegment results = await container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.Metadata, null, null, null, null);
                CloudBlockBlob blob3 = (CloudBlockBlob)results.Results.First();
                Assert.AreEqual(1, blob3.Metadata.Count);
                Assert.AreEqual("value1", blob3.Metadata["key1"]);
                Assert.AreEqual("value1", blob3.Metadata["KEY1"]);

                blob.Metadata.Clear();
                await blob.SetMetadataAsync();

                await blob2.FetchAttributesAsync();
                Assert.AreEqual(0, blob2.Metadata.Count);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload blocks and then commit the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.FuntionalTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadAsync()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                using (MemoryStream wholeBlob = new MemoryStream())
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                    foreach (string block in blocks)
                    {
                        using (MemoryStream memoryStream = new MemoryStream(buffer))
                        {
                            await blob.PutBlockAsync(block, memoryStream, null);
                        }
                        wholeBlob.Write(buffer, 0, buffer.Length);
                    }
                    foreach (string block in extraBlocks)
                    {
                        using (MemoryStream memoryStream = new MemoryStream(buffer))
                        {
                            await blob.PutBlockAsync(block, memoryStream, null);
                        }
                    }

                    await blob.PutBlockListAsync(blocks);

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");

                    using (MemoryOutputStream downloadedBlob = new MemoryOutputStream())
                    {
                        await blob2.DownloadToStreamAsync(downloadedBlob);
                        TestHelper.AssertStreamsAreEqual(wholeBlob, downloadedBlob.UnderlyingStream);
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload a block blob and then verify the block list")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobDownloadBlockListAsync()
        {
            byte[] buffer = GetRandomBuffer(1024);
            List<string> blocks = GetBlockIdList(3);
            List<string> extraBlocks = GetBlockIdList(2);
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                foreach (string block in blocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        await blob.PutBlockAsync(block, memoryStream, null);
                    }
                }
                await blob.PutBlockListAsync(blocks);

                foreach (string block in extraBlocks)
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        await blob.PutBlockAsync(block, memoryStream, null);
                    }
                }

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                await blob2.FetchAttributesAsync();
                Assert.AreEqual(1024 * blocks.Count, blob2.Properties.Length);

                IEnumerable<ListBlockItem> blockList = await blob2.DownloadBlockListAsync();
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsTrue(blockItem.Committed);
                    Assert.IsTrue(blocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, blocks.Count);

                blockList = await blob2.DownloadBlockListAsync(BlockListingFilter.Uncommitted, null, null, null);
                foreach (ListBlockItem blockItem in blockList)
                {
                    Assert.IsFalse(blockItem.Committed);
                    Assert.IsTrue(extraBlocks.Remove(blockItem.Name));
                }
                Assert.AreEqual(0, extraBlocks.Count);

                // Check with 0 length
                blocks = GetBlockIdList(0);
                await blob.PutBlockListAsync(blocks);

                await blob.DownloadBlockListAsync();
                Assert.AreEqual(0, blob.Properties.Length);
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob with invalid options")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamInvalidOptionsAsync()
        {
            BlobRequestOptions options = new BlobRequestOptions()
            {
                UseTransactionalMD5 = true,
                StoreBlobContentMD5 = false,
            };

            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                using (MemoryStream stream = new MemoryStream())
                {
                    await TestHelper.ExpectedExceptionAsync<ArgumentException>(
                        async () => await blob.UploadFromStreamAsync(stream, null, options, null),
                        "Single put blob with mismatching MD5 options should fail immediately");
                }
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamWithAccessConditionAsync()
        {
            OperationContext operationContext = new OperationContext();
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                AccessCondition accessCondition = AccessCondition.GenerateIfNoneMatchCondition("\"*\"");
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, false, 0, true);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 1, 1024);
                await blob.FetchAttributesAsync();
                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                await TestHelper.ExpectedExceptionAsync(
                    async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, true, 0, true),
                    operationContext,
                    "Uploading a blob on top of an existing blob should fail if the ETag matches",
                    HttpStatusCode.PreconditionFailed);
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, true, 0, true);

                blob = container.GetBlockBlobReference("blob3");
                await CreateForTestAsync(blob, 1, 1024);
                await blob.FetchAttributesAsync();
                accessCondition = AccessCondition.GenerateIfMatchCondition(blob.Properties.ETag);
                await TestHelper.ExpectedExceptionAsync(
                    async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, true, 0, true),
                    operationContext,
                    "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                    HttpStatusCode.PreconditionFailed);
                await TestHelper.ExpectedExceptionAsync(
                    async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, false, 0, true),
                    operationContext,
                    "Uploading a blob on top of an non-existing blob should fail when the ETag doesn't match",
                    HttpStatusCode.PreconditionFailed);
                accessCondition = AccessCondition.GenerateIfNoneMatchCondition(blob.Properties.ETag);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, null, accessCondition, operationContext, true, false, 0, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamWithNonSeekableStreamAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, false, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, false, true, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, false, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, false, false, 1024, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamWithSeekableStreamAsyncWithProgress()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, true, 0, true, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, true, 1024, true, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, false, 0, true, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, false, 1024, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, true, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, null, null, null, true, false, 1024, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamLengthSinglePutAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                // Upload 2MB of 5MB stream
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, true, true, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, false, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, false, true, 1024, true);

                // Exclude last byte
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024 - 1, null, null, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024 - 1, null, null, true, true, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024 - 1, null, null, false, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024 - 1, null, null, false, true, 1024, true);

                // Upload exact amount
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024, null, null, true, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024, null, null, true, true, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024, null, null, false, true, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024, null, null, false, true, 1024, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamLengthAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                // Upload 2MB of 5MB stream
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, true, false, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, false, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 2 * 1024 * 1024, null, null, false, false, 1024, true);

                // Exclude last byte
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024 - 1, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024 - 1, null, null, true, false, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024 - 1, null, null, false, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024 - 1, null, null, false, false, 1024, true);

                // Upload exact amount
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024, null, null, true, false, 1024, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 5 * 1024 * 1024, null, null, false, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 5 * 1024 * 1024, 4 * 1024 * 1024, null, null, false, false, 1024, true);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Single put blob and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadFromStreamInvalidLengthAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();
            try
            {
                await TestHelper.ExpectedExceptionAsync<ArgumentOutOfRangeException>(
                        async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, 2 * 1024 * 1024 + 1, null, null, true, true, 0, false),
                        "The given stream does not contain the requested number of bytes from its given position.");

                await TestHelper.ExpectedExceptionAsync<ArgumentOutOfRangeException>(
                        async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, 2 * 1024 * 1024 - 1023, null, null, true, true, 1024, false),
                        "The given stream does not contain the requested number of bytes from its given position.");

                await TestHelper.ExpectedExceptionAsync<ArgumentOutOfRangeException>(
                        async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, 2 * 1024 * 1024 + 1, null, null, false, true, 0, false),
                        "The given stream does not contain the requested number of bytes from its given position.");

                await TestHelper.ExpectedExceptionAsync<ArgumentOutOfRangeException>(
                        async () => await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 2 * 1024 * 1024, 2 * 1024 * 1024 - 1023, null, null, false, true, 1024, false),
                        "The given stream does not contain the requested number of bytes from its given position.");
            }
            finally
            {
                container.DeleteAsync().Wait();
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
            await container.CreateAsync();
            try
            {
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 16 * 1024 * 1024, null, null, null, true, false, 0, true);
                await this.CloudBlockBlobUploadFromStreamAsyncInternal(container, 16 * 1024 * 1024, null, null, null, true, false, 1024, true);
                await this.CloudBlockBlobLargeBlockUploadFromStreamAsync(container, 25 * 1024 * 1024, null, null, null, 0, 5 * 1024 * 1024);
                await this.CloudBlockBlobLargeBlockUploadFromStreamAsync(container, 25 * 1024 * 1024, null, null, null, 1024, 5 * 1024 * 1024);
                await this.CloudBlockBlobLargeBlockUploadFromStreamAsync(container, 32 * 1024 * 1024, null, null, null, 0, 10 * 1024 * 1024);
                await this.CloudBlockBlobLargeBlockUploadFromStreamAsync(container, 32 * 1024 * 1024, null, null, null, 1024 * 1024, 10 * 1024 * 1024);
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload blob using multiple threads and get blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobParallelUploadFromStreamRequestOptionsAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            await container.CreateAsync();

            try
            {
                const int Size = 20 * 1024 * 1024;
                byte[] buffer = GetRandomBuffer(Size);

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                blob.StreamWriteSizeInBytes = 1 * 1024 * 1024;

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob2");
                blob2.StreamWriteSizeInBytes = 1 * 1024 * 1024;

                CloudBlockBlob blob3 = container.GetBlockBlobReference("blob3");
                blob3.StreamWriteSizeInBytes = 1 * 1024 * 1024;

                CloudBlockBlob blob4 = container.GetBlockBlobReference("blob4");
                blob4.StreamWriteSizeInBytes = 5 * 1024 * 1024;

                CloudBlockBlob blob5 = container.GetBlockBlobReference("blob5");
                blob5.StreamWriteSizeInBytes = 7 * 1024 * 1024;

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
                        await blob.UploadFromStreamAsync(sourceStream, null /* accessCondition */, options, context);

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
                        await blob2.UploadFromStreamAsync(sourceStream, null /* accessCondition */, options, context);

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
                        await blob3.UploadFromStreamAsync(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should 1, or 2 if there is a retry
                        Assert.IsTrue(context.RequestResults.Count <= 2);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        options = new BlobRequestOptions()
                        {
                            StoreBlobContentMD5 = false,
                            SingleBlobUploadThresholdInBytes = Size/2,
                            ParallelOperationThreadCount = 3
                        };
                        context = new OperationContext();
                        await blob4.UploadFromStreamAsync(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 5 since StreamWriteSizeInBytes is 5 MB
                        Assert.IsTrue(context.RequestResults.Count >= 5);

                        sourceStream.Seek(0, SeekOrigin.Begin);
                        context = new OperationContext();
                        await blob5.UploadFromStreamAsync(sourceStream, null /* accessCondition */, options, context);

                        // Number of requests should be at least 4 since StreamWriteSizeInBytes is 7 MB
                        Assert.IsTrue(context.RequestResults.Count >= 4);
                    }
                }
            }
            finally
            {
                container.DeleteAsync().Wait();
            }
        }

        private async Task CloudBlockBlobUploadFromStreamAsyncInternal(CloudBlobContainer container, int size, long? copyLength, AccessCondition accessCondition, OperationContext operationContext, bool seekableSourceStream, bool allowSinglePut, int startOffset, bool testMd5, bool trackProgress = false)
        {
            byte[] buffer = GetRandomBuffer(size);

            string md5 = string.Empty;
            if (testMd5)
            {
#if NETCORE
                MD5 hasher = MD5.Create();
                md5 = Convert.ToBase64String(hasher.ComputeHash(buffer, startOffset, copyLength.HasValue ? (int)copyLength : buffer.Length - startOffset));
#else
                CryptographicHash hasher = HashAlgorithmProvider.OpenAlgorithm("MD5").CreateHash();
                hasher.Append(buffer.AsBuffer(startOffset, copyLength.HasValue ? (int)copyLength : buffer.Length - startOffset));
                md5 = CryptographicBuffer.EncodeToBase64String(hasher.GetValueAndReset());
#endif
            }

            CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
            blob.ServiceClient.DefaultRequestOptions.SingleBlobUploadThresholdInBytes = allowSinglePut ? buffer.Length : buffer.Length / 2;
            blob.StreamWriteSizeInBytes = 1 * 1024 * 1024;

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
                    BlobRequestOptions options = new BlobRequestOptions()
                    {
                        StoreBlobContentMD5 = true,
                    };
                    if (copyLength.HasValue)
                    {
                        await blob.UploadFromStreamAsync(sourceStream, copyLength.Value, accessCondition, options, operationContext);
                    }
                    else
                    {
                        await blob.UploadFromStreamAsync(sourceStream, accessCondition, options, operationContext);
                    }
                }

                if (testMd5)
                {
                    await blob.FetchAttributesAsync();
                    Assert.AreEqual(md5, blob.Properties.ContentMD5);
                }
#if !WINDOWS_RT
                if (!trackProgress)

                {
#endif
                    using (MemoryOutputStream downloadedBlobStream = new MemoryOutputStream())
                    {
                        await blob.DownloadToStreamAsync(downloadedBlobStream);
                        Assert.AreEqual(copyLength ?? originalBlobStream.Length, downloadedBlobStream.UnderlyingStream.Length);
                        TestHelper.AssertStreamsAreEqualAtIndex(
                            originalBlobStream,
                            downloadedBlobStream.UnderlyingStream,
                            0,
                            0,
                            copyLength.HasValue ? (int)copyLength : (int)originalBlobStream.Length);
                    }
#if !WINDOWS_RT
                }

                else
                {
                    List<StorageProgress> progressList = new List<StorageProgress>();

                    using (MemoryOutputStream downloadedBlobStream = new MemoryOutputStream())
                    {
                        CancellationToken cancellationToken = new CancellationToken();
                        IProgress<StorageProgress> progressHandler = new Progress<StorageProgress>(progress => progressList.Add(progress));

                        await blob.DownloadToStreamAsync(downloadedBlobStream, null, null, null, progressHandler, cancellationToken);

                        Assert.IsTrue(progressList.Count > 2, "Too few progress received");

                        StorageProgress lastProgress = progressList.Last();

                        Assert.AreEqual(downloadedBlobStream.Length, lastProgress.BytesTransferred, "Final progress has unexpected value");
                    }
                }
#endif
            }
        }

        private async Task CloudBlockBlobLargeBlockUploadFromStreamAsync(CloudBlobContainer container, int size, long? copyLength, AccessCondition accessCondition, OperationContext operationContext, int startOffset, int streamWriteSize)
        {
            byte[] buffer = GetRandomBuffer(size);
            CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
            blob.ServiceClient.DefaultRequestOptions.SingleBlobUploadThresholdInBytes = size / 2;
            blob.StreamWriteSizeInBytes = streamWriteSize;

            using (MemoryStream originalBlobStream = new MemoryStream())
            {
                originalBlobStream.Write(buffer, startOffset, buffer.Length - startOffset);

                Stream sourceStream;
                MemoryStream stream = new MemoryStream(buffer);
                stream.Seek(startOffset, SeekOrigin.Begin);
                sourceStream = stream;

                using (sourceStream)
                {
                    BlobRequestOptions options = new BlobRequestOptions()
                    {
                        StoreBlobContentMD5 = false,
                        ParallelOperationThreadCount = 3,
                        RetryPolicy = new RetryPolicies.NoRetry()
                    };

                    if (copyLength.HasValue)
                    {
                        await blob.UploadFromStreamAsync(sourceStream, copyLength.Value, accessCondition, options, operationContext);
                    }
                    else
                    {
                        await blob.UploadFromStreamAsync(sourceStream, accessCondition, options, operationContext).ConfigureAwait(false);
                    }
                }

                using (MemoryOutputStream downloadedBlobStream = new MemoryOutputStream())
                {
                    await blob.DownloadToStreamAsync(downloadedBlobStream);
                    Assert.AreEqual(copyLength ?? originalBlobStream.Length, downloadedBlobStream.UnderlyingStream.Length);
                    TestHelper.AssertStreamsAreEqualAtIndex(
                        originalBlobStream,
                        downloadedBlobStream.UnderlyingStream,
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
        public async Task CloudBlockBlobSnapshotAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                MemoryStream originalData = new MemoryStream(GetRandomBuffer(1024));
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await blob.UploadFromStreamAsync(originalData);

                Assert.IsFalse(blob.IsSnapshot);
                Assert.IsNull(blob.SnapshotTime, "Root blob has SnapshotTime set");
                Assert.IsFalse(blob.SnapshotQualifiedUri.Query.Contains("snapshot"));
                Assert.AreEqual(blob.Uri, blob.SnapshotQualifiedUri);

                CloudBlockBlob snapshot1 = await blob.CreateSnapshotAsync();
                Assert.AreEqual(blob.Properties.ETag, snapshot1.Properties.ETag);
                Assert.AreEqual(blob.Properties.LastModified, snapshot1.Properties.LastModified);
                Assert.IsTrue(snapshot1.IsSnapshot);
                Assert.IsNotNull(snapshot1.SnapshotTime, "Snapshot does not have SnapshotTime set");
                Assert.AreEqual(blob.Uri, snapshot1.Uri);
                Assert.AreNotEqual(blob.SnapshotQualifiedUri, snapshot1.SnapshotQualifiedUri);
                Assert.AreNotEqual(snapshot1.Uri, snapshot1.SnapshotQualifiedUri);
                Assert.IsTrue(snapshot1.SnapshotQualifiedUri.Query.Contains("snapshot"));

                CloudBlockBlob snapshot2 = await blob.CreateSnapshotAsync();
                Assert.IsTrue(snapshot2.SnapshotTime.Value > snapshot1.SnapshotTime.Value);

                await snapshot1.FetchAttributesAsync();
                await snapshot2.FetchAttributesAsync();
                await blob.FetchAttributesAsync();
                AssertAreEqual(snapshot1.Properties, blob.Properties);

                CloudBlockBlob snapshot1Clone = new CloudBlockBlob(new Uri(blob.Uri + "?snapshot=" + snapshot1.SnapshotTime.Value.ToString("O")), blob.ServiceClient.Credentials);
                Assert.IsNotNull(snapshot1Clone.SnapshotTime, "Snapshot clone does not have SnapshotTime set");
                Assert.AreEqual(snapshot1.SnapshotTime.Value, snapshot1Clone.SnapshotTime.Value);
                await snapshot1Clone.FetchAttributesAsync();
                AssertAreEqual(snapshot1.Properties, snapshot1Clone.Properties);

                CloudBlockBlob snapshotCopy = container.GetBlockBlobReference("blob2");
                await snapshotCopy.StartCopyAsync(TestHelper.Defiddler(snapshot1.Uri));
                await WaitForCopyAsync(snapshotCopy);
                Assert.AreEqual(CopyStatus.Success, snapshotCopy.CopyState.Status);

                await TestHelper.ExpectedExceptionAsync<InvalidOperationException>(
                    async () => await snapshot1.OpenWriteAsync(),
                    "Trying to write to a blob snapshot should fail");

                using (Stream snapshotStream = (await snapshot1.OpenReadAsync()))
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                await blob.PutBlockListAsync(new List<string>());
                await blob.FetchAttributesAsync();

                using (Stream snapshotStream = (await snapshot1.OpenReadAsync()))
                {
                    snapshotStream.Seek(0, SeekOrigin.End);
                    TestHelper.AssertStreamsAreEqual(originalData, snapshotStream);
                }

                BlobResultSegment resultSegment = await container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.All, null, null, null, null);
                List<IListBlobItem> blobs = resultSegment.Results.ToList();
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

        [TestMethod]
        [Description("Create a snapshot with explicit metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobSnapshotMetadataAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 2, 1024);

                blob.Metadata["Hello"] = "World";
                blob.Metadata["Marco"] = "Polo";
                await blob.SetMetadataAsync();

                IDictionary<string, string> snapshotMetadata = new Dictionary<string, string>();
                snapshotMetadata["Hello"] = "Dolly";
                snapshotMetadata["Yoyo"] = "Ma";

                CloudBlockBlob snapshot = await blob.CreateSnapshotAsync(snapshotMetadata, null, null, null);

                // Test the client view against the expected metadata
                // Metadata keys should be case-insensitive
                // None of the original metadata should be present
                Assert.AreEqual("Dolly", snapshot.Metadata["Hello"]);
                Assert.AreEqual("Dolly", snapshot.Metadata["HELLO"]);
                Assert.AreEqual("Ma", snapshot.Metadata["Yoyo"]);
                Assert.IsFalse(snapshot.Metadata.ContainsKey("Marco"));

                // Test the server view against the expected metadata
                await snapshot.FetchAttributesAsync();
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

        [TestMethod]
        [Description("Test conditional access on a blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobConditionalAccessAsync()
        {
            OperationContext operationContext = new OperationContext();
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 2, 1024);
                await blob.FetchAttributesAsync();

                string currentETag = blob.Properties.ETag;
                DateTimeOffset currentModifiedTime = blob.Properties.LastModified.Value;

                // ETag conditional tests
                blob.Metadata["ETagConditionalName"] = "ETagConditionalValue";
                await blob.SetMetadataAsync(AccessCondition.GenerateIfMatchCondition(currentETag), null, null);

                await blob.FetchAttributesAsync();
                string newETag = blob.Properties.ETag;
                Assert.AreNotEqual(newETag, currentETag, "ETage should be modified on write metadata");

                blob.Metadata["ETagConditionalName"] = "ETagConditionalValue2";

                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfNoneMatchCondition(newETag), null, operationContext),
                    operationContext,
                    "If none match on conditional test should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                string invalidETag = "\"0x10101010\"";
                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfMatchCondition(invalidETag), null, operationContext),
                    operationContext,
                    "Invalid ETag on conditional test should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                currentETag = blob.Properties.ETag;
                await blob.SetMetadataAsync(AccessCondition.GenerateIfNoneMatchCondition(invalidETag), null, null);

                await blob.FetchAttributesAsync();
                newETag = blob.Properties.ETag;

                // LastModifiedTime tests
                currentModifiedTime = blob.Properties.LastModified.Value;

                blob.Metadata["DateConditionalName"] = "DateConditionalValue";

                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfModifiedSinceCondition(currentModifiedTime), null, operationContext),
                    operationContext,
                    "IfModifiedSince conditional on current modified time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                DateTimeOffset pastTime = currentModifiedTime.Subtract(TimeSpan.FromMinutes(5));
                await blob.SetMetadataAsync(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null, null);

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromHours(5));
                await blob.SetMetadataAsync(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null, null);

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromDays(5));
                await blob.SetMetadataAsync(AccessCondition.GenerateIfModifiedSinceCondition(pastTime), null, null);

                currentModifiedTime = blob.Properties.LastModified.Value;

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromMinutes(5));
                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null, operationContext),
                    operationContext,
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromHours(5));
                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null, operationContext),
                    operationContext,
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                pastTime = currentModifiedTime.Subtract(TimeSpan.FromDays(5));
                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.SetMetadataAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null, operationContext),
                    operationContext,
                    "IfNotModifiedSince conditional on past time should throw",
                    HttpStatusCode.PreconditionFailed,
                    "ConditionNotMet");

                blob.Metadata["DateConditionalName"] = "DateConditionalValue2";

                currentETag = blob.Properties.ETag;
                await blob.SetMetadataAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(currentModifiedTime), null, null);

                await blob.FetchAttributesAsync();
                newETag = blob.Properties.ETag;
                Assert.AreNotEqual(newETag, currentETag, "ETage should be modified on write metadata");
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Put block boundaries")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobPutBlockBoundariesAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                string blockId = GetBlockIdList(1).First();

                OperationContext operationContext = new OperationContext();
                byte[] buffer = new byte[0];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await blob.PutBlockAsync(blockId, stream, null, null, null, operationContext),
                        operationContext,
                        "Trying to upload a block with zero bytes should fail",
                        HttpStatusCode.BadRequest);
                }

                buffer = new byte[1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await blob.PutBlockAsync(blockId, stream, null);
                }

                buffer = new byte[Constants.MaxBlockSize];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await blob.PutBlockAsync(blockId, stream, null);
                }

                buffer = new byte[Constants.MaxBlockSize + 1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await blob.PutBlockAsync(blockId, stream, null, null, null, operationContext),
                        operationContext,
                        String.Format("Trying to upload a block with more than {0}MB should fail", Constants.MaxBlockSize/Constants.MB),
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

        [TestMethod]
        [Description("Upload blocks and then verify the contents")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobPutBlockAsync()
        {
            byte[] buffer = GetRandomBuffer(4 * 1024 * 1024);
#if NETCORE
            MD5 md5 = MD5.Create();
            string contentMD5 = Convert.ToBase64String(md5.ComputeHash(buffer));
#else
            CryptographicHash hasher = HashAlgorithmProvider.OpenAlgorithm("MD5").CreateHash();
            hasher.Append(buffer.AsBuffer());
            string contentMD5 = CryptographicBuffer.EncodeToBase64String(hasher.GetValueAndReset());
#endif

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                List<string> blockList = GetBlockIdList(2);

                using (MemoryStream resultingData = new MemoryStream())
                {
                    using (MemoryStream memoryStream = new MemoryStream(buffer))
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        await blob.PutBlockAsync(blockList[0], memoryStream, contentMD5);
                        resultingData.Write(buffer, 0, buffer.Length);

                        int offset = buffer.Length - 1024;
                        memoryStream.Seek(offset, SeekOrigin.Begin);
                        OperationContext opContext = new OperationContext();
                        await TestHelper.ExpectedExceptionAsync(
                            async () => await blob.PutBlockAsync(blockList[1], memoryStream, contentMD5, null, null, opContext),
                            opContext,
                            "Invalid MD5 should fail with mismatch",
                            HttpStatusCode.BadRequest,
                            "Md5Mismatch");

                        memoryStream.Seek(offset, SeekOrigin.Begin);
                        await blob.PutBlockAsync(blockList[1], memoryStream, null);
                        resultingData.Write(buffer, offset, buffer.Length - offset);
                    }

                    await blob.PutBlockListAsync(blockList);

                    using (MemoryStream blobData = new MemoryStream())
                    {
                        await blob.DownloadToStreamAsync(blobData);
                        Assert.AreEqual(resultingData.Length, blobData.Length);
                        Assert.IsTrue(blobData.ToArray().SequenceEqual(resultingData.ToArray()));
                    }
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Test block blob methods on a page blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobMethodsOnPageBlobAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                List<string> blobs = await CreateBlobsAsync(container, 1, BlobType.PageBlob);
                CloudBlockBlob blob = container.GetBlockBlobReference(blobs.First());
                List<string> blockList = GetBlockIdList(1);

                OperationContext operationContext = new OperationContext();
                byte[] buffer = new byte[1];
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await blob.PutBlockAsync(blockList.First(), stream, null, null, null, operationContext),
                        operationContext,
                        "Block operations should fail on page blobs",
                        HttpStatusCode.Conflict,
                        "InvalidBlobType");
                }

                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.PutBlockListAsync(blockList, null, null, operationContext),
                    operationContext,
                    "Block operations should fail on page blobs",
                    HttpStatusCode.Conflict,
                    "InvalidBlobType");

                await TestHelper.ExpectedExceptionAsync(
                    async () => await blob.DownloadBlockListAsync(BlockListingFilter.Committed, null, null, operationContext),
                    operationContext,
                    "Block operations should fail on page blobs",
                    HttpStatusCode.Conflict,
                    "InvalidBlobType");
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Test block removal/addition/reordering in a block blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobBlockReorderingAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                List<string> originalBlockIds = GetBlockIdList(10);
                List<string> blockIds = new List<string>(originalBlockIds);
                List<byte[]> blocks = new List<byte[]>();
                for (int i = 0; i < blockIds.Count; i++)
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(i.ToString());
                    using (MemoryStream stream = new MemoryStream(buffer))
                    {
                        await blob.PutBlockAsync(blockIds[i], stream, null);
                    }
                    blocks.Add(buffer);
                }
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("0123456789", await DownloadTextAsync(blob, Encoding.UTF8));

                blockIds.RemoveAt(0);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("123456789", await DownloadTextAsync(blob, Encoding.UTF8));

                blockIds.RemoveAt(8);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("12345678", await DownloadTextAsync(blob, Encoding.UTF8));

                blockIds.RemoveAt(3);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("1235678", await DownloadTextAsync(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[9]))
                {
                    await blob.PutBlockAsync(originalBlockIds[9], stream, null);
                }
                blockIds.Insert(0, originalBlockIds[9]);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("91235678", await DownloadTextAsync(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[0]))
                {
                    await blob.PutBlockAsync(originalBlockIds[0], stream, null);
                }
                blockIds.Add(originalBlockIds[0]);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("912356780", await DownloadTextAsync(blob, Encoding.UTF8));

                using (MemoryStream stream = new MemoryStream(blocks[4]))
                {
                    await blob.PutBlockAsync(originalBlockIds[4], stream, null);
                }
                blockIds.Insert(2, originalBlockIds[4]);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("9142356780", await DownloadTextAsync(blob, Encoding.UTF8));

                blockIds.Insert(0, originalBlockIds[0]);
                await blob.PutBlockListAsync(blockIds);
                Assert.AreEqual("09142356780", await DownloadTextAsync(blob, Encoding.UTF8));
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload and download null/empty data")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadDownloadNoDataAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob");
                await TestHelper.ExpectedExceptionAsync<ArgumentNullException>(
                    async () => await blob.UploadFromStreamAsync(null),
                    "Uploading from a null stream should fail");

                using (MemoryStream stream = new MemoryStream())
                {
                    await blob.UploadFromStreamAsync(stream);
                }

                await TestHelper.ExpectedExceptionAsync<ArgumentNullException>(
                    async () => await blob.DownloadToStreamAsync(null),
                    "Downloading to a null stream should fail");

                using (MemoryStream stream = new MemoryStream())
                {
                    await blob.DownloadToStreamAsync(stream);
                    Assert.AreEqual(0, stream.Length);
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("List committed and uncommitted blobs")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobListUncommittedBlobsAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                List<string> committedBlobs = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    string name = "cblob" + i.ToString();
                    CloudBlockBlob committedBlob = container.GetBlockBlobReference(name);
                    await CreateForTestAsync(committedBlob, 2, 1024);
                    committedBlobs.Add(name);
                }

                List<string> uncommittedBlobs = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string name = "ucblob" + i.ToString();
                    CloudBlockBlob uncommittedBlob = container.GetBlockBlobReference(name);
                    await CreateForTestAsync(uncommittedBlob, 2, 1024, false);
                    uncommittedBlobs.Add(name);
                }

                BlobResultSegment resultSegment = await container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.UncommittedBlobs, null, null, null, null);
                List<IListBlobItem> blobs = resultSegment.Results.ToList();
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
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Upload and download text")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobUploadTextAsync()
        {
            await this.DoTextUploadDownloadAsync("test", false);
            await this.DoTextUploadDownloadAsync("char中文test", true);
            await this.DoTextUploadDownloadAsync("", false);
        }

        private async Task DoTextUploadDownloadAsync(string text, bool checkDifferentEncoding)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateIfNotExistsAsync();
                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");

                // Default Encoding
                await blob.UploadTextAsync(text);
                Assert.AreEqual(text, await blob.DownloadTextAsync());
                if (checkDifferentEncoding)
                {
                    Assert.AreNotEqual(text, await blob.DownloadTextAsync(Encoding.Unicode, null, null, null));
                }

                // Custom Encoding
                await blob.UploadTextAsync(text, Encoding.Unicode, null, null, null);
                Assert.AreEqual(text, await blob.DownloadTextAsync(Encoding.Unicode, null, null, null));
                if (checkDifferentEncoding)
                {
                    Assert.AreNotEqual(text, await blob.DownloadTextAsync());
                }

                // Number of service calls
                OperationContext context = new OperationContext();
#if !FACADE_NETCORE
                await blob.UploadTextAsync(text, null, null, context);
                Assert.AreEqual(1, context.RequestResults.Count);
                await blob.DownloadTextAsync(null, null, context);
                Assert.AreEqual(2, context.RequestResults.Count);
#else
                await blob.UploadTextAsync(text, Encoding.ASCII, null, null, context);
                Assert.AreEqual(1, context.RequestResults.Count);
                await blob.DownloadTextAsync(Encoding.ASCII, null, null, context);
                Assert.AreEqual(2, context.RequestResults.Count);
#endif
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier and fetch attributes")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public async Task CloudBlockBlobSetStandardBlobTierAsync()
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

                    CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                    await CreateForTestAsync(blob, 0, 0);
                    await blob.FetchAttributesAsync();
                    Assert.IsTrue(blob.Properties.StandardBlobTier.HasValue);
                    Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsTrue(blob.Properties.BlobTierInferred.Value);

                    BlobResultSegment listBlobsResult = await container.ListBlobsSegmentedAsync(null);
                    CloudBlockBlob listBlob = (CloudBlockBlob)listBlobsResult.Results.ToList().First();
                    Assert.IsTrue(listBlob.Properties.StandardBlobTier.HasValue);
                    Assert.IsFalse(listBlob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsTrue(listBlob.Properties.BlobTierInferred.Value);

                    await blob.SetStandardBlobTierAsync(blobTier, rehydratePriority, null, null, null, CancellationToken.None);
                    Assert.AreEqual(blobTier, blob.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob.Properties.RehydrationStatus.HasValue);
                    Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob.Properties.BlobTierInferred.Value);

                    CloudBlockBlob blob2 = container.GetBlockBlobReference("blob1");
                    await blob2.FetchAttributesAsync();
                    Assert.AreEqual(blobTier, blob2.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob2.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob2.Properties.RehydrationStatus.HasValue);
                    Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob2.Properties.BlobTierInferred.Value);

                    BlobResultSegment results = await container.ListBlobsSegmentedAsync(null);
                    CloudBlockBlob blob3 = (CloudBlockBlob)results.Results.ToList().First();
                    Assert.AreEqual(blobTier, blob3.Properties.StandardBlobTier.Value);
                    Assert.IsFalse(blob3.Properties.PremiumPageBlobTier.HasValue);
                    Assert.IsFalse(blob3.Properties.RehydrationStatus.HasValue);
                    Assert.IsTrue(blob3.Properties.BlobTierLastModifiedTime.HasValue);
                    Assert.IsFalse(blob3.Properties.BlobTierInferred.HasValue);
                    Assert.AreEqual(blob2.Properties.BlobTierLastModifiedTime.Value, blob3.Properties.BlobTierLastModifiedTime);

                    await blob.DeleteAsync();
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier to archive then rehydrate it to hot and cool")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public async Task CloudBlockBlobRehydrateBlobAsync()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                await container.CreateAsync();

                CloudBlockBlob blob = container.GetBlockBlobReference("blob1");
                await CreateForTestAsync(blob, 0, 0);
                Assert.IsFalse(blob.Properties.BlobTierInferred.HasValue);
                Assert.IsFalse(blob.Properties.StandardBlobTier.HasValue);
                await blob.FetchAttributesAsync();
                Assert.IsTrue(blob.Properties.BlobTierInferred.HasValue);
                Assert.IsTrue(blob.Properties.StandardBlobTier.HasValue);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                await blob.SetStandardBlobTierAsync(StandardBlobTier.Archive);
                Assert.IsNull(blob.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                CloudBlockBlob blob2 = container.GetBlockBlobReference("blob2");
                await CreateForTestAsync(blob2, 0, 0);
                await blob2.SetStandardBlobTierAsync(StandardBlobTier.Archive);
                Assert.IsFalse(blob2.Properties.BlobTierLastModifiedTime.HasValue);

                await blob.SetStandardBlobTierAsync(StandardBlobTier.Cool);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsNull(blob.Properties.RehydrationStatus);
                Assert.IsFalse(blob.Properties.BlobTierLastModifiedTime.HasValue);

                await blob.FetchAttributesAsync();
                Assert.AreEqual(RehydrationStatus.PendingToCool, blob.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob.Properties.StandardBlobTier.Value);
                Assert.IsTrue(blob.Properties.BlobTierLastModifiedTime.HasValue);

                await blob2.SetStandardBlobTierAsync(StandardBlobTier.Hot);
                Assert.AreEqual(StandardBlobTier.Archive, blob2.Properties.StandardBlobTier.Value);
                Assert.IsNull(blob2.Properties.RehydrationStatus);

                await blob2.FetchAttributesAsync();
                Assert.AreEqual(RehydrationStatus.PendingToHot, blob2.Properties.RehydrationStatus);
                Assert.AreEqual(StandardBlobTier.Archive, blob2.Properties.StandardBlobTier.Value);
                Assert.IsTrue(blob2.Properties.BlobTierLastModifiedTime.HasValue);

                CloudBlockBlob listBlob =  (CloudBlockBlob)container.ListBlobsSegmentedAsync(null).Result.Results.ToList().ElementAt(0);
                Assert.AreEqual(StandardBlobTier.Archive, listBlob.Properties.StandardBlobTier.Value);
                Assert.IsFalse(listBlob.Properties.PremiumPageBlobTier.HasValue);
                Assert.AreEqual(RehydrationStatus.PendingToCool, listBlob.Properties.RehydrationStatus.Value);
                Assert.IsTrue(listBlob.Properties.BlobTierLastModifiedTime.HasValue);

                CloudBlockBlob listBlob2 = (CloudBlockBlob)container.ListBlobsSegmentedAsync(null).Result.Results.ToList().ElementAt(1);
                Assert.AreEqual(StandardBlobTier.Archive, listBlob2.Properties.StandardBlobTier.Value);
                Assert.IsFalse(listBlob2.Properties.PremiumPageBlobTier.HasValue);
                Assert.AreEqual(RehydrationStatus.PendingToHot, listBlob2.Properties.RehydrationStatus.Value);

                await blob.DeleteAsync();
                await blob2.DeleteAsync();
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("GetAccountProperties via Block Blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudBlockBlobGetAccountProperties()
        {
            CloudBlobContainer blobContainerWithSAS = GenerateRandomWriteOnlyBlobContainer();
            try
            {
                await blobContainerWithSAS.CreateAsync();

                CloudBlockBlob blob = blobContainerWithSAS.GetBlockBlobReference("test");

                AccountProperties result = await blob.GetAccountPropertiesAsync();

                await blob.DeleteIfExistsAsync();

                Assert.IsNotNull(result);

                Assert.IsNotNull(result.SkuName);

                Assert.IsNotNull(result.AccountKind);
            }
            finally
            {
                blobContainerWithSAS.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Set standard blob tier on copy and fetch attributes")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.BlockBlobOnly)]
        public async Task CloudBlockBlobSetStandardBlobTierOnCopyAsync()
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

                    Random rand = new Random();
                    int randomRangeSize = 500;
                    int randomRangeCount = 10;
                    int sourceSize = rand.Next(0, randomRangeSize) + 1024;
                    int sourceBlockCount = rand.Next(1, randomRangeCount);

                    CloudBlockBlob source = container.GetBlockBlobReference("source");
                    await CreateForTestAsync(source, sourceBlockCount, sourceSize);
                    await source.SetStandardBlobTierAsync(blobTier);

                    CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                    await copy.StartCopyAsync(CloudBlob.SourceBlobToUri(source), null, blobTier, null, null, null, null, null, CancellationToken.None);
                    await WaitForCopyAsync(copy);
                    Assert.AreEqual(blobTier, copy.Properties.StandardBlobTier);

                    Assert.AreEqual(sourceSize * sourceBlockCount, copy.Properties.Length);

                    CloudBlockBlob copyRef = container.GetBlockBlobReference("copy");
                    await copyRef.FetchAttributesAsync();
                    Assert.AreEqual(blobTier, copyRef.Properties.StandardBlobTier);

                    Assert.AreEqual(sourceSize * sourceBlockCount, copyRef.Properties.Length);
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
    }
}


