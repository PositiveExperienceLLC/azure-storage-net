﻿// -----------------------------------------------------------------------------------------
// <copyright file="CopyBlobTest.cs" company="Microsoft">
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
using Microsoft.Azure.Storage.File;
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Storage.Blob
{
    [TestClass]
    public class CopyBlobTest : BlobTestBase
    {
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
        [Description("CopyFromBlob with Unicode source blob")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CopyBlobUsingUnicodeBlobName()
        {
            string _unicodeBlobName = "繁体字14a6c";
            string _nonUnicodeBlobName = "sample_file";

            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();
                CloudBlockBlob blobUnicodeSource = container.GetBlockBlobReference(_unicodeBlobName);
                string data = "Test content";
                UploadText(blobUnicodeSource, data, Encoding.UTF8);
                CloudBlockBlob blobAsciiSource = container.GetBlockBlobReference(_nonUnicodeBlobName);
                UploadText(blobAsciiSource, data, Encoding.UTF8);
                
                //Copy blobs over
                CloudBlockBlob blobAsciiDest = container.GetBlockBlobReference(_nonUnicodeBlobName + "_copy");
                string copyId = blobAsciiDest.StartCopy(TestHelper.Defiddler(blobAsciiSource));
                WaitForCopy(blobAsciiDest);

                CloudBlockBlob blobUnicodeDest = container.GetBlockBlobReference(_unicodeBlobName + "_copy");
                copyId = blobUnicodeDest.StartCopy(TestHelper.Defiddler(blobUnicodeSource));
                WaitForCopy(blobUnicodeDest);

                Assert.AreEqual(CopyStatus.Success, blobUnicodeDest.CopyState.Status);
                Assert.AreEqual(blobUnicodeSource.Uri.AbsolutePath, blobUnicodeDest.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, blobUnicodeDest.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, blobUnicodeDest.CopyState.BytesCopied);
                Assert.AreEqual(copyId, blobUnicodeDest.CopyState.CopyId);
                Assert.IsFalse(blobUnicodeDest.Properties.IsIncrementalCopy);
                Assert.IsTrue(blobUnicodeDest.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        private static void CloudBlockBlobCopy(bool sourceIsSas, bool destinationIsSas, RehydratePriority? rehydratePriority)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                // Create Source on server
                CloudBlockBlob source = container.GetBlockBlobReference("source");

                string data = "String data";
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                // Create Destination on server
                CloudBlockBlob destination = container.GetBlockBlobReference("destination");
                destination.PutBlockList(new string[] { });

                CloudBlockBlob copySource = source;
                CloudBlockBlob copyDestination = destination;
                
                if (sourceIsSas)
                {
                    // Source SAS must have read permissions
                    SharedAccessBlobPermissions permissions = SharedAccessBlobPermissions.Read;
                    SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                    {
                        SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                        SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                        Permissions = permissions,
                    };
                    string sasToken = source.GetSharedAccessSignature(policy);

                    // Get source 
                    StorageCredentials credentials = new StorageCredentials(sasToken);
                    copySource = new CloudBlockBlob(credentials.TransformUri(source.Uri));
                }

                if (destinationIsSas)
                {
                    if (!sourceIsSas)
                    {
                        // Source container must be public if source is not SAS
                        BlobContainerPermissions containerPermissions = new BlobContainerPermissions
                        {
                            PublicAccess = BlobContainerPublicAccessType.Blob
                        };
                        container.SetPermissions(containerPermissions); 
                    }

                    // Destination SAS must have write permissions
                    SharedAccessBlobPermissions permissions = SharedAccessBlobPermissions.Write;
                    SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                    {
                        SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                        SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                        Permissions = permissions,
                    };
                    string sasToken = destination.GetSharedAccessSignature(policy);

                    // Get destination 
                    StorageCredentials credentials = new StorageCredentials(sasToken);
                    copyDestination = new CloudBlockBlob(credentials.TransformUri(destination.Uri));
                }

                // Start copy and wait for completion
                string copyId = copyDestination.StartCopy(TestHelper.Defiddler(copySource), rehydratePriority: rehydratePriority);
                Assert.AreEqual(BlobType.BlockBlob, copyDestination.BlobType);
                WaitForCopy(destination);
                
                // Check original blob references for equality
                Assert.AreEqual(CopyStatus.Success, destination.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, destination.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, destination.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, destination.CopyState.BytesCopied);
                Assert.AreEqual(copyId, destination.CopyState.CopyId);
                Assert.IsFalse(destination.Properties.IsIncrementalCopy);
                Assert.IsTrue(destination.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                if (!destinationIsSas)
                {
                    // Abort Copy is not supported for SAS destination
                    TestHelper.ExpectedException(
                               () => copyDestination.AbortCopy(copyId),
                               "Aborting a copy operation after completion should fail",
                               HttpStatusCode.Conflict,
                               "NoPendingCopyOperation"); 
                }

                source.FetchAttributes();
                Assert.IsNotNull(destination.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, destination.Properties.ETag);
                Assert.IsTrue(destination.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(destination, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not equal.");

                destination.FetchAttributes();
                BlobProperties prop1 = destination.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", destination.Metadata["Test"], false, "Copied metadata not same");

                destination.Delete();
                source.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopySasToSasTest()
        {
            CloudBlockBlobCopy(true, true, default(RehydratePriority));
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyToSasTest()
        {
            CloudBlockBlobCopy(false, true, default(RehydratePriority));
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyFromSasTest()
        {
            CloudBlockBlobCopy(true, false, default(RehydratePriority));
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyTest()
        {
            foreach (var rehydratePriority in new[] { default(RehydratePriority?), RehydratePriority.Standard, RehydratePriority.High })
            {
                CloudBlockBlobCopy(false, false, rehydratePriority);
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyTestAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob source = container.GetBlockBlobReference("source");

                string data = "String data";
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result = copy.BeginStartCopy(TestHelper.Defiddler(source),
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    string copyId = copy.EndStartCopy(result);
                    Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);
                    WaitForCopy(copy);
                    Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                    Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                    Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                    Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                    Assert.AreEqual(copyId, copy.CopyState.CopyId);
                    Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                    Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                    result = copy.BeginAbortCopy(copyId,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    TestHelper.ExpectedException(
                        () => copy.EndAbortCopy(result),
                        "Aborting a copy operation after completion should fail",
                        HttpStatusCode.Conflict,
                        "NoPendingCopyOperation");
                }

                source.FetchAttributes();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        private static void CloudBlockBlobCopyImpl(Func<CloudBlockBlob, string, CloudBlockBlob, string> copyFunc)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudBlockBlob source = container.GetBlockBlobReference("source");

                string data = "String data";
                UploadTextTask(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadataAsync().Wait();
                source.FetchAttributesAsync().Wait();

                CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                string copyId = copyFunc(source, source.Properties.ContentMD5, copy);
                Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);
                WaitForCopyTask(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                TestHelper.ExpectedExceptionTask(
                    copy.AbortCopyAsync(copyId),
                    "Aborting a copy operation after completion should fail",
                    HttpStatusCode.Conflict,
                    "NoPendingCopyOperation");

                source.FetchAttributesAsync().Wait();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadTextTask(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributesAsync().Wait();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.DeleteAsync().Wait();
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyTestTask()
        {
            foreach (var rehydratePriority in new[] { default(RehydratePriority?), RehydratePriority.Standard, RehydratePriority.High })
            {
                CloudBlockBlobCopyImpl((source, sourceContentMD5, copy) => copy.StartCopyAsync(TestHelper.Defiddler(source), default(StandardBlobTier?), rehydratePriority, null, null, null, null, CancellationToken.None).Result);
            }
        }

        [TestMethod]
        [Description("Copy a blob using x-ms-requires-sync, and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        [Ignore]
        public void CloudBlockBlobCopyWithRequiresSyncTestAsync()
        {
            //CloudBlockBlobCopyImpl((source, sourceContentMD5, copy) => copy.StartCopyAsync(TestHelper.Defiddler(source), sourceContentMD5, true /* syncCopy */, default(AccessCondition), default(AccessCondition), default(BlobRequestOptions), default(OperationContext), default(CancellationToken)).Result);
        }

        private static void CloudBlockBlobCopyFromCloudFileImpl(Func<CloudFile, CloudBlockBlob, string> copyFunc)
        {
            CloudFileClient fileClient = GenerateCloudFileClient();

            string name = GetRandomContainerName();
            CloudFileShare share = fileClient.GetShareReference(name);

            CloudBlobContainer container = GetRandomContainerReference();

            try
            {
                try
                {
                    container.CreateAsync().Wait();

                    share.Create();

                    CloudFile source = share.GetRootDirectoryReference().GetFileReference("source");
                    byte[] data = GetRandomBuffer(1024);

                    source.UploadFromByteArray(data, 0, data.Length);

                    source.Metadata["Test"] = "value";
                    source.SetMetadataAsync().Wait();

                    string sasToken = source.GetSharedAccessSignature(new SharedAccessFilePolicy
                    {
                        Permissions = SharedAccessFilePermissions.Read,
                        SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24)
                    });

                    Uri fileSasUri = new Uri(source.StorageUri.PrimaryUri.ToString() + sasToken);

                    source = new CloudFile(fileSasUri);

                    CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                    string copyId = copyFunc(source, copy);
                    Assert.AreEqual(BlobType.BlockBlob, copy.BlobType);

                    try
                    {
                        WaitForCopyTask(copy);

                        Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                        Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                        Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                        Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                        Assert.AreEqual(copyId, copy.CopyState.CopyId);
                        Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                        Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));
                    }
                    catch (NullReferenceException)
                    {
                        // potential null ref in the WaitForCopyTask and CopyState check implementation
                    }

                    TestHelper.ExpectedExceptionTask(
                        copy.AbortCopyAsync(copyId),
                        "Aborting a copy operation after completion should fail",
                        HttpStatusCode.Conflict,
                        "NoPendingCopyOperation");

                    source.FetchAttributesAsync().Wait();
                    Assert.IsNotNull(copy.Properties.ETag);
                    Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                    Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                    byte[] copyData = new byte[source.Properties.Length];

                    copy.DownloadToByteArray(copyData, 0);

                    Assert.IsTrue(data.SequenceEqual(copyData), "Data inside copy of blob not similar");

                    copy.FetchAttributesAsync().Wait();
                    BlobProperties prop1 = copy.Properties;
                    FileProperties prop2 = source.Properties;

                    Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                    Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                    Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                    Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                    Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                    Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                    copy.DeleteIfExistsAsync().Wait();
                }
                finally
                {
                    share.DeleteIfExistsAsync().Wait();
                }
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        [Ignore]
        public void CloudBlockBlobCopyFromCloudFileTestTask()
        {
            //CloudBlockBlobCopyFromCloudFileImpl((source, copy) => copy.StartCopyAsync(TestHelper.Defiddler(source)).Result);
        }
#endif

        [TestMethod]
        [Description("Copy a blob and override metadata during copy")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyTestWithMetadataOverride()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob source = container.GetBlockBlobReference("source");

                string data = "String data";
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                copy.Metadata["Test2"] = "value2";
                string copyId = copy.StartCopy(TestHelper.Defiddler(source));
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributes();
                source.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value2", copy.Metadata["Test2"], false, "Copied metadata not same");
                Assert.IsFalse(copy.Metadata.ContainsKey("Test"), "Source Metadata should not appear in destination blob");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob and override metadata during copy")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyFromSnapshotTest()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob source = container.GetBlockBlobReference("source");
                string data = "String data";
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudBlockBlob snapshot = source.CreateSnapshot();

                //Modify source
                string newData = "Hello";
                source.Metadata["Test"] = "newvalue";
                source.SetMetadata();
                source.Properties.ContentMD5 = null;
                UploadText(source, newData, Encoding.UTF8);

                Assert.AreEqual(newData, DownloadText(source, Encoding.UTF8), "Source is modified correctly");
                Assert.AreEqual(data, DownloadText(snapshot, Encoding.UTF8), "Modifying source blob should not modify snapshot");

                source.FetchAttributes();
                snapshot.FetchAttributes();
                Assert.AreNotEqual(source.Metadata["Test"], snapshot.Metadata["Test"], "Source and snapshot metadata should be independent");

                CloudBlockBlob copy = container.GetBlockBlobReference("copy");
                copy.StartCopy(TestHelper.Defiddler(snapshot));
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(data, DownloadText(copy, Encoding.UTF8), "Data inside copy of blob not similar");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = snapshot.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyTest()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");

                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudPageBlob copy = container.GetPageBlobReference("copy");
                string copyId = copy.StartCopy(TestHelper.Defiddler(source));
                Assert.AreEqual(BlobType.PageBlob, copy.BlobType);
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                TestHelper.ExpectedException(
                    () => copy.AbortCopy(copyId),
                    "Aborting a copy operation after completion should fail",
                    HttpStatusCode.Conflict,
                    "NoPendingCopyOperation");

                source.FetchAttributes();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Perform an incremental of copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobIncrementalCopyTest()
        {
            for (int i = 0; i < 8; i++)
            {
                IncrementCopyTestImpl(i);
            }
        }

        private void IncrementCopyTestImpl(int overload)
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");

                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudPageBlob sourceSnapshot = source.CreateSnapshot();
                System.IO.MemoryStream downloadedBlob = new System.IO.MemoryStream();
                sourceSnapshot.DownloadToStream(downloadedBlob);

                CloudPageBlob copy = container.GetPageBlobReference("copy");

                SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                {
                    SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write,
                };

                string sasToken = sourceSnapshot.GetSharedAccessSignature(policy);

                StorageCredentials blobSAS = new StorageCredentials(sasToken);
                Uri sourceSnapshotUri = blobSAS.TransformUri(TestHelper.Defiddler(sourceSnapshot).SnapshotQualifiedUri);

                StorageCredentials accountSAS = new StorageCredentials(sasToken);
                CloudStorageAccount accountWithSAS = new CloudStorageAccount(accountSAS, source.ServiceClient.StorageUri, null, null, null);
                CloudPageBlob snapshotWithSas = accountWithSAS.CreateCloudBlobClient().GetBlobReferenceFromServer(sourceSnapshot.SnapshotQualifiedUri) as CloudPageBlob;

                string copyId = null;
                if (overload == 0)
                {
                    copyId = copy.StartIncrementalCopy(TestHelper.Defiddler(snapshotWithSas));
                }
                else if (overload == 1)
                {
                    copyId = copy.StartIncrementalCopy(sourceSnapshotUri);
                }
                else if (overload == 2)
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        IAsyncResult result = copy.BeginStartIncrementalCopy(TestHelper.Defiddler(snapshotWithSas), ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        copyId = copy.EndStartIncrementalCopy(result);
                    }
                }
                else if (overload == 3)
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        IAsyncResult result = copy.BeginStartIncrementalCopy(TestHelper.Defiddler(snapshotWithSas), null, null, null, ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        copyId = copy.EndStartIncrementalCopy(result);
                    }
                }
                else if (overload == 4)
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        IAsyncResult result = copy.BeginStartIncrementalCopy(sourceSnapshotUri, null, null, null, ar => waitHandle.Set(), null);
                        waitHandle.WaitOne();
                        copyId = copy.EndStartIncrementalCopy(result);
                    }
                }
                else if (overload == 5)
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        copyId = copy.StartIncrementalCopyAsync(TestHelper.Defiddler(snapshotWithSas)).Result;
                    }
                }
                else if (overload == 6)
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        copyId = copy.StartIncrementalCopyAsync(TestHelper.Defiddler(snapshotWithSas), CancellationToken.None).Result;
                    }
                }
                else
                {
                    using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                    {
                        copyId = copy.StartIncrementalCopyAsync(TestHelper.Defiddler(snapshotWithSas), null, null, null, CancellationToken.None).Result;
                    }
                }

                Assert.AreEqual(BlobType.PageBlob, copy.BlobType);
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsTrue(copy.Properties.IsIncrementalCopy);
                Assert.IsTrue(copy.CopyState.DestinationSnapshotTime.HasValue);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                TestHelper.ExpectedException(
                    () => copy.AbortCopy(copyId),
                    "Aborting a copy operation after completion should fail",
                    HttpStatusCode.Conflict,
                    "NoPendingCopyOperation");

                source.FetchAttributes();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                TestHelper.ExpectedException(
                    () => DownloadText(copy, Encoding.UTF8),
                    "The specified operation is not allowed on an incremental copy blob.",
                    HttpStatusCode.Conflict,
                    "OperationNotAllowedOnIncrementalCopyBlob");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                TestHelper.ExpectedException(
                    () => copy.Delete(),
                    "This operation is not permitted because the blob has snapshots.",
                    HttpStatusCode.Conflict,
                    "SnapshotsPresent");
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyTestAPM()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");

                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudPageBlob copy = container.GetPageBlobReference("copy");
                using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                {
                    IAsyncResult result = copy.BeginStartCopy(TestHelper.Defiddler(source),
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    string copyId = copy.EndStartCopy(result);
                    Assert.AreEqual(BlobType.PageBlob, copy.BlobType);
                    WaitForCopy(copy);
                    Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                    Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                    Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                    Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                    Assert.AreEqual(copyId, copy.CopyState.CopyId);
                    Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                    result = copy.BeginAbortCopy(copyId,
                        ar => waitHandle.Set(),
                        null);
                    waitHandle.WaitOne();
                    TestHelper.ExpectedException(
                        () => copy.EndAbortCopy(result),
                        "Aborting a copy operation after completion should fail",
                        HttpStatusCode.Conflict,
                        "NoPendingCopyOperation");
                }

                source.FetchAttributes();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

#if TASK
        [TestMethod]
        [Description("Copy a blob and then verify its contents, properties, and metadata")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyTestTask()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.CreateAsync().Wait();

                CloudPageBlob source = container.GetPageBlobReference("source");

                string data = new string('a', 512);
                UploadTextTask(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadataAsync().Wait();

                CloudPageBlob copy = container.GetPageBlobReference("copy");
                string copyId = copy.StartCopyAsync(TestHelper.Defiddler(source)).Result;
                Assert.AreEqual(BlobType.PageBlob, copy.BlobType);
                WaitForCopyTask(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                TestHelper.ExpectedExceptionTask(
                    copy.AbortCopyAsync(copyId),
                    "Aborting a copy operation after completion should fail",
                    HttpStatusCode.Conflict,
                    "NoPendingCopyOperation");

                source.FetchAttributesAsync().Wait();
                Assert.IsNotNull(copy.Properties.ETag);
                Assert.AreNotEqual(source.Properties.ETag, copy.Properties.ETag);
                Assert.IsTrue(copy.Properties.LastModified > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadTextTask(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributesAsync().Wait();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.DeleteAsync().Wait();
            }
            finally
            {
                container.DeleteIfExistsAsync().Wait();
            }
        }
#endif

        [TestMethod]
        [Description("Copy a blob and override metadata during copy")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyTestWithMetadataOverride()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");

                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudPageBlob copy = container.GetPageBlobReference("copy");
                copy.Metadata["Test2"] = "value2";
                string copyId = copy.StartCopy(TestHelper.Defiddler(source));
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(source.Uri.AbsolutePath, copy.CopyState.Source.AbsolutePath);
                Assert.AreEqual(data.Length, copy.CopyState.TotalBytes);
                Assert.AreEqual(data.Length, copy.CopyState.BytesCopied);
                Assert.AreEqual(copyId, copy.CopyState.CopyId);
                Assert.IsFalse(copy.Properties.IsIncrementalCopy);
                Assert.IsTrue(copy.CopyState.CompletionTime > DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

                string copyData = DownloadText(copy, Encoding.UTF8);
                Assert.AreEqual(data, copyData, "Data inside copy of blob not similar");

                copy.FetchAttributes();
                source.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = source.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value2", copy.Metadata["Test2"], false, "Copied metadata not same");
                Assert.IsFalse(copy.Metadata.ContainsKey("Test"), "Source Metadata should not appear in destination blob");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob and override metadata during copy")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyFromSnapshotTest()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");
                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);

                source.Metadata["Test"] = "value";
                source.SetMetadata();

                CloudPageBlob snapshot = source.CreateSnapshot();

                //Modify source
                string newData = new string('b', 512);
                source.Metadata["Test"] = "newvalue";
                source.SetMetadata();
                source.Properties.ContentMD5 = null;
                UploadText(source, newData, Encoding.UTF8);

                Assert.AreEqual(newData, DownloadText(source, Encoding.UTF8), "Source is modified correctly");
                Assert.AreEqual(data, DownloadText(snapshot, Encoding.UTF8), "Modifying source blob should not modify snapshot");

                source.FetchAttributes();
                snapshot.FetchAttributes();
                Assert.AreNotEqual(source.Metadata["Test"], snapshot.Metadata["Test"], "Source and snapshot metadata should be independent");

                CloudPageBlob copy = container.GetPageBlobReference("copy");
                copy.StartCopy(TestHelper.Defiddler(snapshot));
                WaitForCopy(copy);
                Assert.AreEqual(CopyStatus.Success, copy.CopyState.Status);
                Assert.AreEqual(data, DownloadText(copy, Encoding.UTF8), "Data inside copy of blob not similar");

                copy.FetchAttributes();
                BlobProperties prop1 = copy.Properties;
                BlobProperties prop2 = snapshot.Properties;

                Assert.AreEqual(prop1.CacheControl, prop2.CacheControl);
                Assert.AreEqual(prop1.ContentEncoding, prop2.ContentEncoding);
                Assert.AreEqual(prop1.ContentDisposition, prop2.ContentDisposition);
                Assert.AreEqual(prop1.ContentLanguage, prop2.ContentLanguage);
                Assert.AreEqual(prop1.ContentMD5, prop2.ContentMD5);
                Assert.AreEqual(prop1.ContentType, prop2.ContentType);

                Assert.AreEqual("value", copy.Metadata["Test"], false, "Copied metadata not same");

                copy.Delete();
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob with source access condition")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudBlockBlobCopyWithSourceAccessCondition()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudBlockBlob source = container.GetBlockBlobReference("source");
                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);
                string validLeaseId = Guid.NewGuid().ToString();
                string leaseId = source.AcquireLease(TimeSpan.FromSeconds(60), validLeaseId);
                string invalidLeaseId = Guid.NewGuid().ToString();

                source.FetchAttributes();
                AccessCondition sourceAccessCondition1 = AccessCondition.GenerateIfNotModifiedSinceCondition(source.Properties.LastModified.Value);
                CloudBlockBlob copy1 = container.GetBlockBlobReference("copy1");
                copy1.StartCopy(TestHelper.Defiddler(source), sourceAccessCondition: sourceAccessCondition1);
                WaitForCopy(copy1);
                Assert.AreEqual(CopyStatus.Success, copy1.CopyState.Status);

                AccessCondition sourceAccessCondition2 = AccessCondition.GenerateLeaseCondition(invalidLeaseId);
                CloudBlockBlob copy2 = container.GetBlockBlobReference("copy2");
                TestHelper.ExpectedException<ArgumentException>(() => copy2.StartCopy(TestHelper.Defiddler(source), sourceAccessCondition: sourceAccessCondition2), "A lease condition cannot be specified on the source of a copy.");          
            }
            finally
            {
                container.DeleteIfExists();
            }
        }

        [TestMethod]
        [Description("Copy a blob with source access condition")]
        [TestCategory(ComponentCategory.Blob)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevStore), TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudPageBlobCopyWithSourceAccessCondition()
        {
            CloudBlobContainer container = GetRandomContainerReference();
            try
            {
                container.Create();

                CloudPageBlob source = container.GetPageBlobReference("source");
                string data = new string('a', 512);
                UploadText(source, data, Encoding.UTF8);
                string validLeaseId = Guid.NewGuid().ToString();
                string leaseId = source.AcquireLease(TimeSpan.FromSeconds(60), validLeaseId);
                string invalidLeaseId = Guid.NewGuid().ToString();

                source.FetchAttributes();
                AccessCondition sourceAccessCondition1 = AccessCondition.GenerateIfNotModifiedSinceCondition(source.Properties.LastModified.Value);
                CloudPageBlob copy1 = container.GetPageBlobReference("copy1");
                copy1.StartCopy(TestHelper.Defiddler(source), sourceAccessCondition1);
                WaitForCopy(copy1);
                Assert.AreEqual(CopyStatus.Success, copy1.CopyState.Status);

                AccessCondition sourceAccessCondition2 = AccessCondition.GenerateLeaseCondition(invalidLeaseId);
                CloudPageBlob copy2 = container.GetPageBlobReference("copy2");
                TestHelper.ExpectedException<ArgumentException>(() => copy2.StartCopy(TestHelper.Defiddler(source), sourceAccessCondition2), "A lease condition cannot be specified on the source of a copy.");
            }
            finally
            {
                container.DeleteIfExists();
            }
        }
    }
}
