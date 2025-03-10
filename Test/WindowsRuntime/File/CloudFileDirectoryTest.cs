﻿// -----------------------------------------------------------------------------------------
// <copyright file="CloudFileDirectoryTest.cs" company="Microsoft">
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

namespace Microsoft.Azure.Storage.File
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Azure.Storage.Core;
    using Microsoft.Azure.Storage.Core.Util;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    [TestClass]
    public class CloudFileDirectoryTest : FileTestBase
    {
        //
        // Use TestInitialize to run code before running each test 
        [TestInitialize()]
        public void MyTestInitialize()
        {
            if (TestBase.FileBufferManager != null)
            {
                TestBase.FileBufferManager.OutstandingBufferCount = 0;
            }
        }
        //
        // Use TestCleanup to run code after each test has run
        [TestCleanup()]
        public void MyTestCleanup()
        {
            if (TestBase.FileBufferManager != null)
            {
                Assert.AreEqual(0, TestBase.FileBufferManager.OutstandingBufferCount);
            }
        }

        private async Task<bool> CloudFileDirectorySetupAsync(CloudFileShare share)
        {
            try
            {
                CloudFileDirectory rootDirectory = share.GetRootDirectoryReference();
                for (int i = 1; i < 3; i++)
                {
                    CloudFileDirectory topDirectory = rootDirectory.GetDirectoryReference("TopDir" + i);
                    await topDirectory.CreateAsync();

                    for (int j = 1; j < 3; j++)
                    {
                        CloudFileDirectory midDirectory = topDirectory.GetDirectoryReference("MidDir" + j);
                        await midDirectory.CreateAsync();

                        for (int k = 1; k < 3; k++)
                        {
                            CloudFileDirectory endDirectory = midDirectory.GetDirectoryReference("EndDir" + k);
                            await endDirectory.CreateAsync();

                            CloudFile file1 = endDirectory.GetFileReference("EndFile" + k);
                            await file1.CreateAsync(0);
                        }
                    }

                    CloudFile file2 = topDirectory.GetFileReference("File" + i);
                    await file2.CreateAsync(0);
                }

                return true;
            }
            catch (Exception e)
            {
                throw e;
            }

        }

        [TestMethod]
        [Description("Create a directory and then delete it")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCreateAndDeleteTask()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();

            try
            {
                // Arrange
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");

                // Act
                await directory.CreateAsync();

                // Assert
                Assert.IsNotNull(directory.Properties.FilePermissionKey);
                Assert.IsNotNull(directory.Properties.NtfsAttributes);
                Assert.IsNotNull(directory.Properties.CreationTime);
                Assert.IsNotNull(directory.Properties.LastWriteTime);
                Assert.IsNotNull(directory.Properties.ChangeTime);
                Assert.IsNotNull(directory.Properties.DirectoryId);
                Assert.IsNotNull(directory.Properties.ParentId);

                Assert.IsNull(directory.Properties.filePermissionKeyToSet);
                Assert.IsNull(directory.Properties.ntfsAttributesToSet);
                Assert.IsNull(directory.Properties.creationTimeToSet);
                Assert.IsNull(directory.Properties.lastWriteTimeToSet);
                Assert.IsNull(directory.FilePermission);

            }
            finally
            {
                await share.DeleteAsync();
            }
        }

        [TestMethod]
        [Description("Create a directory with a file permission key")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCreateFilePermissionKeyTask()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                // Arrange
                await share.CreateAsync();
                string permission = "O:S-1-5-21-2127521184-1604012920-1887927527-21560751G:S-1-5-21-2127521184-1604012920-1887927527-513D:AI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;S-1-5-21-397955417-626881126-188441444-3053964)";
                string permissionKey = await share.CreateFilePermissionAsync(permission);
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                directory.Properties.FilePermissionKey = permissionKey;

                // Act
                await directory.CreateAsync();

                // Assert
                Assert.IsNotNull(directory.Properties.FilePermissionKey);
                Assert.IsNotNull(directory.Properties.NtfsAttributes);
                Assert.IsNotNull(directory.Properties.CreationTime);
                Assert.IsNotNull(directory.Properties.LastWriteTime);
                Assert.IsNotNull(directory.Properties.ChangeTime);
                Assert.IsNotNull(directory.Properties.DirectoryId);
                Assert.IsNotNull(directory.Properties.ParentId);

                Assert.IsNull(directory.Properties.filePermissionKeyToSet);
                Assert.IsNull(directory.Properties.ntfsAttributesToSet);
                Assert.IsNull(directory.Properties.creationTimeToSet);
                Assert.IsNull(directory.Properties.lastWriteTimeToSet);
                Assert.IsNull(directory.FilePermission);

            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Create a directory with multiple parameters")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCreateMultibleParametersTask()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                // Arrange
                await share.CreateAsync();
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");

                string permissions = "O:S-1-5-21-2127521184-1604012920-1887927527-21560751G:S-1-5-21-2127521184-1604012920-1887927527-513D:AI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;S-1-5-21-397955417-626881126-188441444-3053964)";
                CloudFileNtfsAttributes attributes = CloudFileNtfsAttributes.Directory | CloudFileNtfsAttributes.Archive | CloudFileNtfsAttributes.NoScrubData | CloudFileNtfsAttributes.Offline;
                DateTimeOffset creationTime = DateTimeOffset.UtcNow.AddDays(-1);
                DateTimeOffset lastWriteTime = DateTimeOffset.UtcNow;

                directory.FilePermission = permissions;
                directory.Properties.CreationTime = creationTime;
                directory.Properties.LastWriteTime = lastWriteTime;
                directory.Properties.NtfsAttributes  = attributes;

                // Act
                await directory.CreateAsync();

                // Assert
                Assert.IsNotNull(directory.Properties.FilePermissionKey);
                Assert.AreEqual(attributes, directory.Properties.NtfsAttributes);
                Assert.AreEqual(creationTime, directory.Properties.CreationTime);
                Assert.AreEqual(lastWriteTime, directory.Properties.LastWriteTime);

                Assert.IsNotNull(directory.Properties.ChangeTime);
                Assert.IsNotNull(directory.Properties.DirectoryId);
                Assert.IsNotNull(directory.Properties.ParentId);

                Assert.IsNull(directory.Properties.filePermissionKeyToSet);
                Assert.IsNull(directory.Properties.ntfsAttributesToSet);
                Assert.IsNull(directory.Properties.creationTimeToSet);
                Assert.IsNull(directory.Properties.lastWriteTimeToSet);
                Assert.IsNull(directory.FilePermission);
            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Try to create an existing directory")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCreateIfNotExistsAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();

            try
            {
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                Assert.IsTrue(await directory.CreateIfNotExistsAsync());
                Assert.IsFalse(await directory.CreateIfNotExistsAsync());
                await directory.DeleteAsync();
                Assert.IsTrue(await directory.CreateIfNotExistsAsync());
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Calling CreateIfNotExistsAsync on an existing root directory should return false")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileExistingRootDirectoryCreateIfNotExistsAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();

            try
            {
                CloudFileDirectory rootDirectory = share.GetRootDirectoryReference();
                Assert.IsFalse(await rootDirectory.CreateIfNotExistsAsync());
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Calling CreateIfNotExistsAsync on a nonexistent share's root directory should result in an error 404")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileNonexistentRootDirectoryCreateIfNotExistsAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.DeleteIfExistsAsync();

            CloudFileDirectory rootDirectory = share.GetRootDirectoryReference();
            OperationContext context = new OperationContext();
            await TestHelper.ExpectedExceptionAsync(
                async () => await rootDirectory.CreateIfNotExistsAsync(null, context), 
                context,
                "Calling CreateIfNotExistsAsync on a nonexistent root directory should have resulted in an error 404",
                HttpStatusCode.NotFound);
        }

        [TestMethod]
        [Description("Verify that a file directory's metadata can be updated")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectorySetMetadataAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                await share.CreateAsync();

                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                await directory.CreateAsync();

                CloudFileDirectory directory2 = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                await directory2.FetchAttributesAsync();
                Assert.AreEqual(0, directory2.Metadata.Count);

                directory.Metadata["key1"] = null;
                OperationContext context = new OperationContext();
                await TestHelper.ExpectedExceptionAsync(
                    async () => await directory.SetMetadataAsync(null /* accessConditions */, null /* options */, context),
                    context,
                    "Metadata keys should have a non-null value",
                    HttpStatusCode.Unused);

                directory.Metadata["key1"] = "";
                await TestHelper.ExpectedExceptionAsync(
                    async () => await directory.SetMetadataAsync(null /* accessConditions */, null /* options */, context),
                    context,
                    "Metadata keys should have a non-empty value",
                    HttpStatusCode.Unused);

                directory.Metadata["key1"] = "value1";
                await directory.SetMetadataAsync(null /* accessConditions */, null /* options */, context);

                await directory2.FetchAttributesAsync();
                Assert.AreEqual(1, directory2.Metadata.Count);
                Assert.AreEqual("value1", directory2.Metadata["key1"]);
                // Metadata keys should be case-insensitive
                Assert.AreEqual("value1", directory2.Metadata["KEY1"]);

                directory.Metadata.Clear();
                await directory.SetMetadataAsync(null /* accessConditions */, null /* options */, context);

                await directory2.FetchAttributesAsync();
                Assert.AreEqual(0, directory2.Metadata.Count);
            }
            finally
            {
                share.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Verify that a file directory's properties can be updated")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectorySetPropertiesTask()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                // Arrange
                await share.CreateAsync();
                CloudFileDirectory dir1 = share.GetRootDirectoryReference().GetDirectoryReference("mydir");
                await dir1.CreateAsync();

                var attributes = CloudFileNtfsAttributes.Directory | CloudFileNtfsAttributes.NotContentIndexed;
                var creationTime = DateTimeOffset.UtcNow.AddDays(-1);
                var lastWriteTime = DateTimeOffset.UtcNow;

                dir1.Properties.filePermissionKey = null;
                dir1.FilePermission = "O:S-1-5-21-2127521184-1604012920-1887927527-21560751G:S-1-5-21-2127521184-1604012920-1887927527-513D:AI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;S-1-5-21-397955417-626881126-188441444-3053964)";
                dir1.Properties.NtfsAttributes = attributes;
                dir1.Properties.CreationTime = creationTime;
                dir1.Properties.LastWriteTime = lastWriteTime;

                // Act
                await dir1.SetPropertiesAsync();

                // Assert
                Assert.IsNotNull(dir1.Properties.FilePermissionKey);
                Assert.AreEqual(attributes, dir1.Properties.NtfsAttributes);
                Assert.AreEqual(creationTime, dir1.Properties.CreationTime);
                Assert.AreEqual(lastWriteTime, dir1.Properties.LastWriteTime);

                Assert.IsNull(dir1.Properties.filePermissionKeyToSet);
                Assert.IsNull(dir1.Properties.ntfsAttributesToSet);
                Assert.IsNull(dir1.Properties.creationTimeToSet);
                Assert.IsNull(dir1.Properties.lastWriteTimeToSet);
                Assert.IsNull(dir1.FilePermission);

                // Act
                CloudFileDirectory dir2 = share.GetRootDirectoryReference().GetDirectoryReference("mydir");
                await dir2.FetchAttributesAsync();

                // Assert
                Assert.AreEqual(dir1.Properties.filePermissionKey, dir2.Properties.filePermissionKey);
                Assert.AreEqual(dir1.Properties.ntfsAttributes, dir2.Properties.ntfsAttributes);
                Assert.AreEqual(dir1.Properties.creationTime, dir2.Properties.creationTime);
                Assert.AreEqual(dir1.Properties.lastWriteTime, dir2.Properties.lastWriteTime);

                Assert.IsNull(dir2.Properties.filePermissionKeyToSet);
                Assert.IsNull(dir2.Properties.ntfsAttributesToSet);
                Assert.IsNull(dir2.Properties.creationTimeToSet);
                Assert.IsNull(dir2.Properties.lastWriteTimeToSet);
                Assert.IsNull(dir2.FilePermission);
            }
            finally
            {
                await share.DeleteAsync();
            }
        }

        [TestMethod]
        [Description("Verify setting the properties of a file with file permissions key")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectorySetPropertiesFilePermissionsKeyTask()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                // Arrange
                await share.CreateAsync();

                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("dir1");
                await directory.CreateAsync();

                Thread.Sleep(1000);

                string permission = "O:S-1-5-21-2127521184-1604012920-1887927527-21560751G:S-1-5-21-2127521184-1604012920-1887927527-513D:AI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;S-1-5-21-397955417-626881126-188441444-3053964)";
                string permissionKey = await share.CreateFilePermissionAsync(permission);

                CloudFileDirectory directory2 = share.GetRootDirectoryReference().GetDirectoryReference("dir1");

                directory2.Properties.FilePermissionKey = permissionKey;

                // Act
                await directory2.SetPropertiesAsync();

                // Assert
                Assert.IsNotNull(directory2.Properties.FilePermissionKey);
                Assert.IsNull(directory2.Properties.filePermissionKeyToSet);

                // Act
                CloudFileDirectory directory3 = share.GetRootDirectoryReference().GetDirectoryReference("dir1");
                await directory3.FetchAttributesAsync();

                // Assert - also making sure attributes, creation time, and last-write time were preserved
                Assert.AreEqual(permissionKey, directory3.Properties.FilePermissionKey);
                Assert.AreEqual(directory2.Properties.FilePermissionKey, directory3.Properties.FilePermissionKey);
                Assert.AreEqual(directory.Properties.NtfsAttributes, directory3.Properties.NtfsAttributes);
                Assert.AreEqual(directory.Properties.CreationTime, directory3.Properties.CreationTime);
                Assert.AreEqual(directory.Properties.LastWriteTime, directory3.Properties.LastWriteTime);

                // This block is just for checking that file permission is preserved
                // Arrange
                directory2 = share.GetRootDirectoryReference().GetDirectoryReference("dir1");
                DateTimeOffset creationTime = DateTime.UtcNow.AddDays(-2);
                directory2.Properties.creationTime = creationTime;

                // Act
                await directory2.SetPropertiesAsync();
                directory3 = share.GetRootDirectoryReference().GetDirectoryReference("dir1");
                await directory3.FetchAttributesAsync();

                // Assert
                Assert.AreEqual(permissionKey, directory3.Properties.FilePermissionKey);
            }
            finally
            {
                await share.DeleteAsync();
            }
        }

        [TestMethod]
        [Description("Create a directory and verify its SMB handles can be checked.")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryListHandlesNullCaseTask()
        {
            // TODO add non-zero test cases if OpenHandle is ever available over REST 
            CloudFileShare share = GetRandomShareReference();

            try
            {
                await share.CreateAsync();
                CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference("mydir");
                await dir.CreateAsync();

                share = await share.SnapshotAsync();
                dir = share.GetRootDirectoryReference().GetDirectoryReference("mydir");

                FileContinuationToken token = null;
                List<FileHandle> handles = new List<FileHandle>();

                do
                {
                    FileHandleResultSegment response = await dir.ListHandlesSegmentedAsync(token, null, null, null, null, null, CancellationToken.None);
                    handles.AddRange(response.Results);
                    token = response.ContinuationToken;
                } while (token.NextMarker != null);

                Assert.AreEqual(0, handles.Count);
            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Create a directory and verify its SMB handles can be closed.")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCloseAllHandlesTask()
        {
            // TODO add non-zero test cases if OpenHandle is ever available over REST 
            CloudFileShare share = GetRandomShareReference();

            try
            {
                await share.CreateAsync();
                CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference("mydir");
                await dir.CreateAsync();

                FileContinuationToken token = null;
                int handlesClosed = 0;

                do
                {
                    CloseFileHandleResultSegment response = await dir.CloseAllHandlesSegmentedAsync(token, null, null, null, null, CancellationToken.None);
                    handlesClosed += response.NumHandlesClosed;
                    token = response.ContinuationToken;
                } while (token != null && token.NextMarker != null);

                Assert.AreEqual(handlesClosed, 0);
            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Create a directory and verify its SMB handles can be closed.")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCloseHandleTask()
        {
            // TODO add non-zero test cases if OpenHandle is ever available over REST 
            CloudFileShare share = GetRandomShareReference();

            try
            {
                await share.CreateAsync();
                CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference("mydir");
                await dir.CreateAsync();

                share = await share.SnapshotAsync();
                dir = share.GetRootDirectoryReference().GetDirectoryReference("mydir"); ;

                FileContinuationToken token = null;
                int handlesClosed = 0;
                const string nonexistentHandle = "12345";

                do
                {
                    CloseFileHandleResultSegment response = await dir.CloseHandleSegmentedAsync(nonexistentHandle, token);
                    handlesClosed += response.NumHandlesClosed;
                    token = response.ContinuationToken;
                } while (token != null && token.NextMarker != null);

                Assert.AreEqual(handlesClosed, 0);
            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }

        [TestMethod]
        [Description("Try to delete a non-existing directory")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryDeleteIfExistsAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();

            try
            {
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                Assert.IsFalse(await directory.DeleteIfExistsAsync());
                await directory.CreateAsync();
                Assert.IsTrue(await directory.DeleteIfExistsAsync());
                Assert.IsFalse(await directory.DeleteIfExistsAsync());
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("CloudFileDirectory listing")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryListFilesAndDirectoriesAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            try
            {
                await share.CreateAsync();
                if (await CloudFileDirectorySetupAsync(share))
                {
                    CloudFileDirectory topDir1 = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1");
                    IEnumerable<IListFileItem> list1 = await ListFilesAndDirectoriesAsync(topDir1, null, null, null, null);

                    List<IListFileItem> simpleList1 = list1.ToList();
                    ////Check if for 3 because if there were more than 3, the previous assert would have failed.
                    ////So the only thing we need to make sure is that it is not less than 3. 
                    Assert.IsTrue(simpleList1.Count == 3);

                    IListFileItem item11 = simpleList1.ElementAt(0);
                    Assert.IsTrue(item11.Uri.Equals(share.Uri + "/TopDir1/File1"));
                    Assert.AreEqual("File1", ((CloudFile)item11).Name);

                    IListFileItem item12 = simpleList1.ElementAt(1);
                    Assert.IsTrue(item12.Uri.Equals(share.Uri + "/TopDir1/MidDir1"));
                    Assert.AreEqual("MidDir1", ((CloudFileDirectory)item12).Name);

                    IListFileItem item13 = simpleList1.ElementAt(2);
                    Assert.IsTrue(item13.Uri.Equals(share.Uri + "/TopDir1/MidDir2"));
                    CloudFileDirectory midDir2 = (CloudFileDirectory)item13;
                    Assert.AreEqual("MidDir2", ((CloudFileDirectory)item13).Name);

                    IEnumerable<IListFileItem> list2 = await ListFilesAndDirectoriesAsync(midDir2, null, null, null, null);

                    List<IListFileItem> simpleList2 = list2.ToList();
                    Assert.IsTrue(simpleList2.Count == 2);

                    IListFileItem item21 = simpleList2.ElementAt(0);
                    Assert.IsTrue(item21.Uri.Equals(share.Uri + "/TopDir1/MidDir2/EndDir1"));
                    Assert.AreEqual("EndDir1", ((CloudFileDirectory)item21).Name);

                    IListFileItem item22 = simpleList2.ElementAt(1);
                    Assert.IsTrue(item22.Uri.Equals(share.Uri + "/TopDir1/MidDir2/EndDir2"));
                    Assert.AreEqual("EndDir2", ((CloudFileDirectory)item22).Name);
                }
            }
            finally
            {
                share.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("CloudFileDirectory listing with prefix")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryListFilesAndDirectoriesWithPrefixAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            try
            {
                await share.CreateAsync();
                if (await CloudFileDirectorySetupAsync(share))
                {
                    CloudFileDirectory topDir1 = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1");

                    IEnumerable<IListFileItem> results = await ListFilesAndDirectoriesAsync(topDir1, "file");
                    List<IListFileItem> list = results.ToList();
                    Assert.IsTrue(list.Count == 1);
                    IListFileItem item = list.ElementAt(0);
                    Assert.IsTrue(item.Uri.Equals(share.Uri + "/TopDir1/File1"));
                    Assert.AreEqual("File1", ((CloudFile)item).Name);

                    results = await ListFilesAndDirectoriesAsync( topDir1, "mid");
                    list = results.ToList(); 
                    Assert.IsTrue(list.Count == 2);
                    IListFileItem item1 = list.ElementAt(0);
                    IListFileItem item2 = list.ElementAt(1);
                    Assert.IsTrue(item1.Uri.Equals(share.Uri + "/TopDir1/MidDir1"));
                    Assert.AreEqual("MidDir1", ((CloudFileDirectory)item1).Name);
                    Assert.IsTrue(item2.Uri.Equals(share.Uri + "/TopDir1/MidDir2"));
                    Assert.AreEqual("MidDir2", ((CloudFileDirectory)item2).Name);

                    results = await ListFilesAndDirectoriesAsync( 
                        topDir1 /* directory */,
                        "mid" /* prefix */,
                        1 /* maxCount */,
                        null /* options */,
                        null /* operationContext */);
                    
                    list = results.ToList(); 
                    Assert.IsTrue(list.Count() == 2);
                    item1 = list.ElementAt(0);
                    item2 = list.ElementAt(1);

                    Assert.IsTrue(item1.Uri.Equals(share.Uri + "/TopDir1/MidDir1"));
                    Assert.AreEqual("MidDir1", ((CloudFileDirectory)item1).Name);
                    Assert.IsTrue(item2.Uri.Equals(share.Uri + "/TopDir1/MidDir2"));
                    Assert.AreEqual("MidDir2", ((CloudFileDirectory)item2).Name);
                }
            }
            finally
            {
                share.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("CloudFileDirectory deleting a directory that has subdirectories and files")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryWithFilesDeleteAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            try
            {
                await share.CreateAsync();
                if (await CloudFileDirectorySetupAsync(share))
                {
                    CloudFileDirectory dir1 = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1/MidDir1/EndDir1");
                    CloudFile file1 = dir1.GetFileReference("EndFile1");
                    OperationContext context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir1.DeleteAsync(null, null, context),
                        context,
                        "Delete a non-empty directory",
                        HttpStatusCode.Conflict);

                    await file1.DeleteAsync();
                    await dir1.DeleteAsync();
                    Assert.IsFalse(await file1.ExistsAsync());
                    Assert.IsFalse(await dir1.ExistsAsync());
                }
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        /*
        [TestMethod]
        [Description("CloudFileDirectory deleting a directory using conditional access")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryConditionalAccessAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            try
            {
                await share.CreateAsync();
                if (await CloudFileDirectorySetupAsync(share))
                {
                    CloudFileDirectory dir1 = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1/MidDir1/EndDir1/");
                    CloudFile file1 = dir1.GetFileReference("EndFile1");
                    await file1.DeleteAsync();
                    await dir1.FetchAttributesAsync();
                    string etag = dir1.Properties.ETag;

                    OperationContext context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir1.DeleteAsync(AccessCondition.GenerateIfNoneMatchCondition(etag), null, context),
                        context,
                        "If none match on conditional test should throw",
                        HttpStatusCode.PreconditionFailed,
                        "ConditionNotMet");

                    string invalidETag = "\"0x10101010\"";

                    context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir1.DeleteAsync(AccessCondition.GenerateIfMatchCondition(invalidETag), null, context),
                        context,
                        "If none match on conditional test should throw",
                        HttpStatusCode.PreconditionFailed,
                        "ConditionNotMet");

                    await dir1.DeleteAsync(AccessCondition.GenerateIfMatchCondition(etag), null, null);

                    // LastModifiedTime tests
                    CloudFileDirectory dir2 = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1/MidDir1/EndDir2/");
                    CloudFile file2 = dir2.GetFileReference("EndFile2");
                    await file2.DeleteAsync();
                    await dir2.FetchAttributesAsync();
                    DateTimeOffset currentModifiedTime = dir2.Properties.LastModified.Value;

                    context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir2.DeleteAsync(AccessCondition.GenerateIfModifiedSinceCondition(currentModifiedTime), null, context),
                        context,
                        "IfModifiedSince conditional on current modified time should throw",
                        HttpStatusCode.PreconditionFailed,
                        "ConditionNotMet");

                    DateTimeOffset pastTime = currentModifiedTime.Subtract(TimeSpan.FromMinutes(5));
                    context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir2.DeleteAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(pastTime), null, context),
                        context,
                        "IfNotModifiedSince conditional on past time should throw",
                        HttpStatusCode.PreconditionFailed,
                        "ConditionNotMet");

                    DateTimeOffset ancientTime = currentModifiedTime.Subtract(TimeSpan.FromDays(5));
                    context = new OperationContext();
                    await TestHelper.ExpectedExceptionAsync(
                        async () => await dir2.DeleteAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(ancientTime), null, context),
                        context,
                        "IfNotModifiedSince conditional on past time should throw",
                        HttpStatusCode.PreconditionFailed,
                        "ConditionNotMet");

                    await dir2.DeleteAsync(AccessCondition.GenerateIfNotModifiedSinceCondition(currentModifiedTime), null, null);
                }
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }
        */

        [TestMethod]
        [Description("CloudFileDirectory creating a file without creating the directory")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryFileCreateWithoutDirectoryAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);
            CloudFileDirectory rootDirectory = share.GetRootDirectoryReference();

            try
            {
                await share.CreateAsync();
                CloudFileDirectory dir = rootDirectory.GetDirectoryReference("Dir1");
                CloudFile file = dir.GetFileReference("file1");
                OperationContext context = new OperationContext();
                await TestHelper.ExpectedExceptionAsync(
                    async () => await file.CreateAsync(0, null, null, context),
                    context,
                    "Creating a file when the directory has not been created should throw",
                    HttpStatusCode.NotFound,
                    "ParentNotFound");

                // File creation directly in the share should pass.
                CloudFile file2 = rootDirectory.GetFileReference("file2");
                await file2.CreateAsync(0);

                await dir.CreateAsync();
                await file.CreateAsync(0);
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("CloudFileDirectory creating subdirectory when the parent directory ahsn't been created yet")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryCreateDirectoryUsingPrefixAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            try
            {
                await share.CreateAsync();
                CloudFileDirectory dir1 = share.GetRootDirectoryReference().GetDirectoryReference("Dir1");
                CloudFileDirectory dir2 = share.GetRootDirectoryReference().GetDirectoryReference("Dir1/Dir2");
                OperationContext context = new OperationContext();
                await TestHelper.ExpectedExceptionAsync(
                    async () => await dir2.CreateAsync(null, context),
                    context,
                    "Try to create directory hierarchy by specifying prefix",
                    HttpStatusCode.NotFound);

                await dir1.CreateAsync();
                await dir2.CreateAsync();
            }
            finally
            {
                share.DeleteAsync().Wait();
            }
        }

        [TestMethod]
        [Description("CloudFileDirectory get parent of File")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryGetParentAsync()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);
            try
            {
                await share.CreateAsync();
                CloudFile file = share.GetRootDirectoryReference().GetDirectoryReference("Dir1").GetFileReference("File1");
                Assert.AreEqual("File1", file.Name);

                // get the file's parent
                CloudFileDirectory parent = file.Parent;
                Assert.AreEqual(parent.Name, "Dir1");

                // get share as parent
                CloudFileDirectory root = parent.Parent;
                Assert.AreEqual(root.Name, "");

                // make sure the parent of the share dir is null
                CloudFileDirectory empty = root.Parent;
                Assert.IsNull(empty);

                // from share, get directory reference to share
                root = share.GetRootDirectoryReference();
                Assert.AreEqual("", root.Name);
                Assert.AreEqual(share.Uri.AbsoluteUri, root.Uri.AbsoluteUri);

                // make sure the parent of the share dir is null
                empty = root.Parent;
                Assert.IsNull(empty);
            }
            finally
            {
                share.DeleteIfExistsAsync().Wait();
            }
        }

        [TestMethod]
        [Description("Get subdirectory and then traverse back to parent")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryGetSubdirectoryAndTraverseBackToParent()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1");
            CloudFileDirectory subDirectory = directory.GetDirectoryReference("MidDir1");
            CloudFileDirectory parent = subDirectory.Parent;
            Assert.AreEqual(parent.Name, directory.Name);
            Assert.AreEqual(parent.Uri, directory.Uri);
        }

        [TestMethod]
        [Description("Get parent on root")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryGetParentOnRoot()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            CloudFileDirectory root = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1/");
            CloudFileDirectory parent = root.Parent;
            Assert.IsNotNull(parent);

            CloudFileDirectory empty = parent.Parent;
            Assert.IsNull(empty);
        }

        [TestMethod]
        [Description("Hierarchical traversal")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryHierarchicalTraversal()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            ////Traverse hierarchically starting with length 1
            CloudFileDirectory directory1 = share.GetRootDirectoryReference().GetDirectoryReference("Dir1");
            CloudFileDirectory subdir1 = directory1.GetDirectoryReference("Dir2");
            CloudFileDirectory parent1 = subdir1.Parent;
            Assert.AreEqual(parent1.Name, directory1.Name);

            CloudFileDirectory subdir2 = subdir1.GetDirectoryReference("Dir3");
            CloudFileDirectory parent2 = subdir2.Parent;
            Assert.AreEqual(parent2.Name, subdir1.Name);

            CloudFileDirectory subdir3 = subdir2.GetDirectoryReference("Dir4");
            CloudFileDirectory parent3 = subdir3.Parent;
            Assert.AreEqual(parent3.Name, subdir2.Name);

            CloudFileDirectory subdir4 = subdir3.GetDirectoryReference("Dir5");
            CloudFileDirectory parent4 = subdir4.Parent;
            Assert.AreEqual(parent4.Name, subdir3.Name);
        }

        [TestMethod]
        [Description("Get directory parent for file")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryFileParentValidate()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            CloudFile file = share.GetRootDirectoryReference().GetFileReference("TopDir1/MidDir1/EndDir1/EndFile1");
            CloudFileDirectory directory = file.Parent;
            Assert.AreEqual(directory.Name, "EndDir1");
            Assert.AreEqual(directory.Uri, share.Uri + "/TopDir1/MidDir1/EndDir1");
        }

        [TestMethod]
        [Description("Get a reference to an empty sub-directory")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryGetEmptySubDirectory()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            CloudFileDirectory root = share.GetRootDirectoryReference().GetDirectoryReference("TopDir1/");
            TestHelper.ExpectedException<ArgumentException>(
                () => root.GetDirectoryReference(String.Empty),
                "Try to get a reference to an empty sub-directory");
        }

        [TestMethod]
        [Description("Using absolute Uri string should just append to the base uri")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public void CloudFileDirectoryAbsoluteUriAppended()
        {
            CloudFileClient client = GenerateCloudFileClient();
            string name = GetRandomShareName();
            CloudFileShare share = client.GetShareReference(name);

            CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference(share.Uri.AbsoluteUri);
            Assert.AreEqual(NavigationHelper.AppendPathToSingleUri(share.Uri, share.Uri.AbsoluteUri), dir.Uri);
            Assert.AreEqual(new Uri(share.Uri + "/" + share.Uri.AbsoluteUri), dir.Uri);

            dir = share.GetRootDirectoryReference().GetDirectoryReference(share.Uri.AbsoluteUri + "/TopDir1");
            Assert.AreEqual(NavigationHelper.AppendPathToSingleUri(share.Uri, share.Uri.AbsoluteUri + "/TopDir1"), dir.Uri);
        }

        [TestMethod]
        [Description("Test CloudFileDirectory APIs within a share snapshot")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryApisInShareSnapshotAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();
            CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference("dir1");
            await dir.CreateAsync();
            dir.Metadata["key1"] = "value1";
            await dir.SetMetadataAsync(null, null, null);
            CloudFileShare snapshot = await share.SnapshotAsync();

            CloudFileDirectory snapshotDir = snapshot.GetRootDirectoryReference().GetDirectoryReference("dir1");
            dir.Metadata["key2"] = "value2";
            await dir.SetMetadataAsync(null, null, null);
            await snapshotDir.FetchAttributesAsync();

            Assert.IsTrue(snapshotDir.Metadata.Count == 1 && snapshotDir.Metadata["key1"].Equals("value1"));
            // Metadata keys should be case-insensitive
            Assert.IsTrue(snapshotDir.Metadata["KEY1"].Equals("value1"));
            Assert.IsNotNull(snapshotDir.Properties.ETag);

            await dir.FetchAttributesAsync();
            Assert.IsTrue(dir.Metadata.Count == 2 && dir.Metadata["key2"].Equals("value2"));
            // Metadata keys should be case-insensitive
            Assert.IsTrue(dir.Metadata["KEY2"].Equals("value2"));
            Assert.IsNotNull(dir.Properties.ETag);
            Assert.AreNotEqual(dir.Properties.ETag, snapshotDir.Properties.ETag);

            await snapshot.DeleteAsync();
            await share.DeleteAsync();
        }

        [TestMethod]
        [Description("Test CloudFileDirectory APIs within a share snapshot")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryApisInvalidApisInShareSnapshotAsync()
        {
            CloudFileShare share = GetRandomShareReference();
            await share.CreateAsync();
            CloudFileShare snapshot = await share.SnapshotAsync();
            CloudFileDirectory dir = snapshot.GetRootDirectoryReference().GetDirectoryReference("dir1");

            try
            {
                dir.CreateAsync().Wait();
                Assert.Fail("API should fail in a snapshot");
            }
            catch (InvalidOperationException e)
            {
                Assert.AreEqual(SR.CannotModifyShareSnapshot, e.Message);
            }
            try
            {
                dir.DeleteAsync().Wait();
                Assert.Fail("API should fail in a snapshot");
            }
            catch (InvalidOperationException e)
            {
                Assert.AreEqual(SR.CannotModifyShareSnapshot, e.Message);
            }
            try
            {
                dir.SetMetadataAsync(null, null, null).Wait();
                Assert.Fail("API should fail in a snapshot");
            }
            catch (InvalidOperationException e)
            {
                Assert.AreEqual(SR.CannotModifyShareSnapshot, e.Message);
            }

            snapshot.DeleteAsync().Wait();
            share.DeleteAsync().Wait();
        }

        [TestMethod]
        [Description("Verify the attributes of a directory")]
        [TestCategory(ComponentCategory.File)]
        [TestCategory(TestTypeCategory.UnitTest)]
        [TestCategory(SmokeTestCategory.NonSmoke)]
        [TestCategory(TenantTypeCategory.DevFabric), TestCategory(TenantTypeCategory.Cloud)]
        public async Task CloudFileDirectoryFetchAttributesTask()
        {
            CloudFileShare share = GetRandomShareReference();
            try
            {
                // Arrange
                await share.CreateAsync();
                CloudFileDirectory directory = share.GetRootDirectoryReference().GetDirectoryReference("directory1");
                await directory.CreateAsync();
                CloudFileDirectory directory2 = share.GetRootDirectoryReference().GetDirectoryReference("directory1");

                // Act
                await directory2.FetchAttributesAsync();

                // Assert
                Assert.AreEqual(directory.Properties.ETag, directory2.Properties.ETag);
                Assert.AreEqual(directory.Properties.LastModified, directory2.Properties.LastModified);
                Assert.AreEqual(directory.Properties.FilePermissionKey, directory2.Properties.FilePermissionKey);
                Assert.AreEqual(directory.Properties.NtfsAttributes, directory2.Properties.NtfsAttributes);
                Assert.AreEqual(directory.Properties.CreationTime, directory2.Properties.CreationTime);
                Assert.AreEqual(directory.Properties.LastWriteTime, directory2.Properties.LastWriteTime);
                Assert.AreEqual(directory.Properties.ChangeTime, directory2.Properties.ChangeTime);
                Assert.AreEqual(directory.Properties.DirectoryId, directory2.Properties.DirectoryId);
                Assert.AreEqual(directory.Properties.ParentId, directory2.Properties.ParentId);
            }
            finally
            {
                await share.DeleteIfExistsAsync();
            }
        }
    }
}
