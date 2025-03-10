﻿// -----------------------------------------------------------------------------------------
// <copyright file="CloudBlockBlob.cs" company="Microsoft">
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

namespace Microsoft.Azure.Storage.Blob
{
    using Microsoft.Azure.Storage.Blob.Protocol;
    using Microsoft.Azure.Storage.Core;
    using Microsoft.Azure.Storage.Core.Executor;
    using Microsoft.Azure.Storage.Core.Util;
    using Microsoft.Azure.Storage.Shared.Protocol;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public partial class CloudBlockBlob : CloudBlob, ICloudBlob
    {
        /// <summary>
        /// Opens a stream for writing to the blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <returns>A stream to be used for writing to the blob.</returns>
        /// <remarks>
        /// <para>Note that this method always makes a call to the <see cref="CloudBlob.FetchAttributesAsync(AccessCondition, BlobRequestOptions, OperationContext, CancellationToken)"/> method under the covers.</para>
        /// <para>Set the <see cref="StreamWriteSizeInBytes"/> property before calling this method to specify the block size to write, in bytes, 
        /// ranging from between 16 KB and 100 MB inclusive.</para>
        /// <para>To throw an exception if the blob exists instead of overwriting it, see <see cref="OpenWriteAsync(AccessCondition, BlobRequestOptions, OperationContext)"/>.</para>        
        /// </remarks>
        public virtual Task<CloudBlobStream> OpenWriteAsync()
        {
            return this.OpenWriteAsync(null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Opens a stream for writing to the blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A stream to be used for writing to the blob.</returns>
        /// <remarks>
        /// <para>Note that this method always makes a call to the <see cref="CloudBlob.FetchAttributesAsync(AccessCondition, BlobRequestOptions, OperationContext, CancellationToken)"/> method under the covers.</para>
        /// <para>Set the <see cref="StreamWriteSizeInBytes"/> property before calling this method to specify the block size to write, in bytes, 
        /// ranging from between 16 KB and 100 MB inclusive.</para>
        /// <para>To throw an exception if the blob exists instead of overwriting it, pass in an <see cref="AccessCondition"/>
        /// object generated using <see cref="AccessCondition.GenerateIfNotExistsCondition"/>.</para>
        /// </remarks>
        public virtual Task<CloudBlobStream> OpenWriteAsync(AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.OpenWriteAsync(accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Opens a stream for writing to the blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A stream to be used for writing to the blob.</returns>
        /// <remarks>
        /// <para>Note that this method always makes a call to the <see cref="CloudBlob.FetchAttributesAsync(AccessCondition, BlobRequestOptions, OperationContext, CancellationToken)"/> method under the covers.</para>
        /// <para>Set the <see cref="StreamWriteSizeInBytes"/> property before calling this method to specify the block size to write, in bytes, 
        /// ranging from between 16 KB and 100 MB inclusive.</para>
        /// <para>To throw an exception if the blob exists instead of overwriting it, pass in an <see cref="AccessCondition"/>
        /// object generated using <see cref="AccessCondition.GenerateIfNotExistsCondition"/>.</para>
        /// </remarks>
        public virtual async Task<CloudBlobStream> OpenWriteAsync(AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            this.attributes.AssertNoSnapshot();
            operationContext = operationContext ?? new OperationContext();
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, this.BlobType, this.ServiceClient, false);

            if ((accessCondition != null) && accessCondition.IsConditional)
            {
                try
                {
                    // If the accessCondition is IsIfNotExists, the fetch call will always return 400
                    await this.FetchAttributesAsync(accessCondition.Clone().RemoveIsIfNotExistsCondition(), options, operationContext, cancellationToken).ConfigureAwait(false);

                    // In case the blob already exists and the access condition is "IfNotExists", we should fail fast before uploading any content for the blob 
                    if (accessCondition.IsIfNotExists)
                    {
                        throw GenerateExceptionForConflictFailure();
                    }
                }
                catch (Exception)
                {
                    if ((operationContext.LastResult != null) &&
                        (((operationContext.LastResult.HttpStatusCode == (int)HttpStatusCode.NotFound) &&
                          string.IsNullOrEmpty(accessCondition.IfMatchETag)) ||
                         (operationContext.LastResult.HttpStatusCode == (int)HttpStatusCode.Forbidden)))
                    {
                        // If we got a 404 and the condition was not an If-Match OR if we got a 403,
                        // we should continue with the operation.
                    }
                    else
                    {
                        throw;
                    }
                }

                CloudBlobStream stream = new BlobWriteStream(this, accessCondition, modifiedOptions, operationContext);
                return stream;

            }
            else
            {
                CloudBlobStream stream = new BlobWriteStream(this, accessCondition, modifiedOptions, operationContext);
                return stream;
            }
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source)
        {
            return this.UploadFromStreamAsyncHelper(source, null /* length*/, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, long length)
        {
            return this.UploadFromStreamAsyncHelper(source, length, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadFromStreamAsyncHelper(source, null /* length */, accessCondition, options, operationContext);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, long length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadFromStreamAsyncHelper(source, length, accessCondition, options, operationContext);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            return this.UploadFromStreamAsyncHelper(source, null /* length */, accessCondition, options, operationContext, progressHandler, cancellationToken);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.UploadFromStreamAsyncHelper(source, null /* length */, accessCondition, options, operationContext, cancellationToken);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, long length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            return this.UploadFromStreamAsyncHelper(source, length, accessCondition, options, operationContext, progressHandler, cancellationToken);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromStreamAsync(Stream source, long length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.UploadFromStreamAsyncHelper(source, length, accessCondition, options, operationContext, cancellationToken);
        }

        /// <summary>
        /// Uploads a stream to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        internal Task UploadFromStreamAsyncHelper(Stream source, long? length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadFromStreamAsyncHelper(source, length, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a stream to a block blob. 
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        internal async Task UploadFromStreamAsyncHelper(Stream source, long? length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            await this.UploadFromStreamAsyncHelper(source, length, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a stream to a block blob. 
        /// </summary>
        /// <param name="source">The stream providing the blob content.</param>
        /// <param name="length">The number of bytes to write from the source stream at its current position.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        private async Task UploadFromStreamAsyncHelper(Stream source, long? length, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            CommonUtility.AssertNotNull("source", source);

            Stream sourceAsStream = source;
            if (length.HasValue)
            {
                CommonUtility.AssertInBounds("length", length.Value, 1);

                if (sourceAsStream.CanSeek && length > sourceAsStream.Length - sourceAsStream.Position)
                {
                    throw new ArgumentOutOfRangeException("length", SR.StreamLengthShortError);
                }
            }

            this.CheckAdjustBlockSize(length ?? (source.CanSeek ? (source.Length - source.Position) : length));
            this.attributes.AssertNoSnapshot();
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            operationContext = operationContext ?? new OperationContext();

            ExecutionState<NullType> tempExecutionState = BlobCommonUtility.CreateTemporaryExecutionState(modifiedOptions);

            bool lessThanSingleBlobThreshold = sourceAsStream.CanSeek
                                               && (length ?? sourceAsStream.Length - sourceAsStream.Position)
                                               <= modifiedOptions.SingleBlobUploadThresholdInBytes;
            if (modifiedOptions.ParallelOperationThreadCount == 1 && lessThanSingleBlobThreshold)
            {
                Checksum contentChecksum;

                if (modifiedOptions.ChecksumOptions.StoreContentMD5.Value || modifiedOptions.ChecksumOptions.StoreContentCRC64.Value)
                {
                    StreamDescriptor streamCopyState = new StreamDescriptor();
                    long startPosition = sourceAsStream.Position;
                    await sourceAsStream.WriteToAsync(Stream.Null, this.ServiceClient.BufferManager, length, null /* maxLength */, new ChecksumRequested(md5: modifiedOptions.ChecksumOptions.StoreContentMD5.Value, crc64: modifiedOptions.ChecksumOptions.StoreContentCRC64.Value), tempExecutionState, streamCopyState, cancellationToken).ConfigureAwait(false);
                    sourceAsStream.Position = startPosition;
                    
                    contentChecksum = new Checksum(md5: streamCopyState.Md5, crc64: streamCopyState.Crc64);
                }
                else
                {
                    if (modifiedOptions.ChecksumOptions.UseTransactionalMD5.Value)
                    {
                        throw new ArgumentException(SR.PutBlobNeedsStoreBlobContentMD5, "options");
                    }
                    if (modifiedOptions.ChecksumOptions.UseTransactionalCRC64.Value)
                    {
                        throw new ArgumentException(SR.PutBlobNeedsStoreBlobContentCRC64, "options");
                    }

                    contentChecksum = Checksum.None;
                }

                await Executor.ExecuteAsyncNullReturn(
                this.PutBlobImpl(new AggregatingProgressIncrementer(progressHandler).CreateProgressIncrementingStream(sourceAsStream), length, contentChecksum, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken).ConfigureAwait(false);
            }
            else
            {
                bool useOpenWrite = !source.CanSeek
                      || this.streamWriteSizeInBytes < Constants.MinLargeBlockSize
                      || (modifiedOptions.ChecksumOptions.StoreContentMD5.HasValue && modifiedOptions.ChecksumOptions.StoreContentMD5.Value)
                      || (modifiedOptions.ChecksumOptions.StoreContentCRC64.HasValue && modifiedOptions.ChecksumOptions.StoreContentCRC64.Value);

                if (useOpenWrite)
                {
                    using (CloudBlobStream blobStream = await this.OpenWriteAsync(accessCondition, options, operationContext, cancellationToken).ConfigureAwait(false))
                    {
                        // We should always call AsStreamForWrite with bufferSize=0 to prevent buffering. Our
                        // stream copier only writes 64K buffers at a time anyway, so no buffering is needed.
                        await sourceAsStream.WriteToAsync(blobStream, this.ServiceClient.BufferManager, length, null /* maxLength */, ChecksumRequested.None, tempExecutionState, null /* streamCopyState */, cancellationToken).ConfigureAwait(false);
                        await blobStream.CommitAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    // Synchronization mutex required to ensure thread-safe, concurrent operations on related SubStream instances.
                    SemaphoreSlim streamReadThrottler = new SemaphoreSlim(1);
                    await this.UploadFromMultiStreamAsync(this.OpenMultiSubStream(source, length, streamReadThrottler), accessCondition, modifiedOptions, operationContext, new AggregatingProgressIncrementer(progressHandler), cancellationToken).ConfigureAwait(false);
                }
            }

        }

        /// <summary>
        /// Uploads a file to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="path">A string containing the file path providing the blob content.</param>
        /// <returns>A <see cref="Task"/> object that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromFileAsync(string path)
        {
            return this.UploadFromFileAsync(path, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a file to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="path">A string containing the file path providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromFileAsync(string path, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadFromFileAsync(path, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a file to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="path">A string containing the file path providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual async Task UploadFromFileAsync(string path, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            await this.UploadFromFileAsync(path, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a file to a block blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="path">A string containing the file path providing the blob content.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual async Task UploadFromFileAsync(string path, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            CommonUtility.AssertNotNull("path", path);

            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);

            // Determines whether to use the normal, single-stream upload approach or the new parallel, multi-stream strategy.
            bool useSingleStream = this.streamWriteSizeInBytes < Constants.MinLargeBlockSize
                                   || (modifiedOptions.ChecksumOptions.StoreContentMD5.HasValue && modifiedOptions.ChecksumOptions.StoreContentMD5.Value)
                                   || (modifiedOptions.ChecksumOptions.StoreContentCRC64.HasValue && modifiedOptions.ChecksumOptions.StoreContentCRC64.Value);


            if (useSingleStream)
            {
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    await this.UploadFromStreamAsync(stream, accessCondition, modifiedOptions, operationContext, progressHandler, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                CheckAdjustBlockSize(new FileInfo(path).Length);
                await this.UploadFromMultiStreamAsync(OpenMultiFileStream(path), accessCondition, modifiedOptions, operationContext, new AggregatingProgressIncrementer(progressHandler), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uploads the contents of a byte array to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="buffer">An array of bytes.</param>
        /// <param name="index">The zero-based byte offset in buffer at which to begin uploading bytes to the blob.</param>
        /// <param name="count">The number of bytes to be written to the blob.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromByteArrayAsync(byte[] buffer, int index, int count)
        {
            return this.UploadFromByteArrayAsync(buffer, index, count, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads the contents of a byte array to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="buffer">An array of bytes.</param>
        /// <param name="index">The zero-based byte offset in buffer at which to begin uploading bytes to the blob.</param>
        /// <param name="count">The number of bytes to be written to the blob.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromByteArrayAsync(byte[] buffer, int index, int count, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadFromByteArrayAsync(buffer, index, count, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads the contents of a byte array to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="buffer">An array of bytes.</param>
        /// <param name="index">The zero-based byte offset in buffer at which to begin uploading bytes to the blob.</param>
        /// <param name="count">The number of bytes to be written to the blob.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromByteArrayAsync(byte[] buffer, int index, int count, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.UploadFromByteArrayAsync(buffer, index, count, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken);
        }

        /// <summary>
        /// Uploads the contents of a byte array to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="buffer">An array of bytes.</param>
        /// <param name="index">The zero-based byte offset in buffer at which to begin uploading bytes to the blob.</param>
        /// <param name="count">The number of bytes to be written to the blob.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadFromByteArrayAsync(byte[] buffer, int index, int count, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            CommonUtility.AssertNotNull("buffer", buffer);

            SyncMemoryStream stream = new SyncMemoryStream(buffer, index, count);
            return this.UploadFromStreamAsync(stream, accessCondition, options, operationContext, progressHandler, cancellationToken);

        }

        /// <summary>
        /// Uploads a string of text to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="content">The text to upload, encoded as a UTF-8 string.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadTextAsync(string content)
        {
            return this.UploadTextAsync(content, null /* encoding */, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a string of text to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="content">The text to upload.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        [DoesServiceRequest]
        public virtual Task UploadTextAsync(string content, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadTextAsync(content, null /* encoding */, accessCondition, options, operationContext);
        }

        /// <summary>
        /// Uploads a string of text to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="content">The text to upload.</param>
        /// <param name="encoding">A <see cref="System.Text.Encoding"/> object that indicates the text encoding to use. If <c>null</c>, UTF-8 will be used.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadTextAsync(string content, Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.UploadTextAsync(content, encoding, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a string of text to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="content">The text to upload, encoded as a UTF-8 string.</param>
        /// <param name="encoding">A <see cref="System.Text.Encoding"/> object that indicates the text encoding to use. If <c>null</c>, UTF-8 will be used.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadTextAsync(string content, Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.UploadTextAsync(content, encoding, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken);
        }

        /// <summary>
        /// Uploads a string of text to a blob. If the blob already exists, it will be overwritten.
        /// </summary>
        /// <param name="content">The text to upload, encoded as a UTF-8 string.</param>
        /// <param name="encoding">A <see cref="System.Text.Encoding"/> object that indicates the text encoding to use. If <c>null</c>, UTF-8 will be used.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task UploadTextAsync(string content, Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            CommonUtility.AssertNotNull("content", content);

            byte[] contentAsBytes = (encoding ?? Encoding.UTF8).GetBytes(content);
            return this.UploadFromByteArrayAsync(contentAsBytes, 0, contentAsBytes.Length, accessCondition, options, operationContext, progressHandler, cancellationToken);
        }

        /// <summary>
        /// Downloads the blob's contents as a string.
        /// </summary>
        /// <returns>The contents of the blob, as a string.</returns>
        [DoesServiceRequest]
        public virtual Task<string> DownloadTextAsync()
        {
            return this.DownloadTextAsync(null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Downloads the blob's contents as a string.
        /// </summary>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>The contents of the blob, as a string.</returns>
        [DoesServiceRequest]
        public virtual Task<string> DownloadTextAsync(AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.DownloadTextAsync(null /* encoding */, accessCondition, options, operationContext);
        }

        /// <summary>
        /// Downloads the blob's contents as a string.
        /// </summary>
        /// <param name="encoding">An object that indicates the text encoding to use.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>The contents of the blob, as a string.</returns>
        [DoesServiceRequest]
        public virtual Task<string> DownloadTextAsync(Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.DownloadTextAsync(encoding, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Downloads the blob's contents as a string.
        /// </summary>
        /// <param name="encoding">An object that indicates the text encoding to use.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>The contents of the blob, as a string.</returns>
        [DoesServiceRequest]
        public virtual Task<string> DownloadTextAsync(Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.DownloadTextAsync(encoding, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken);
        }

        /// <summary>
        /// Downloads the blob's contents as a string.
        /// </summary>
        /// <param name="encoding">An object that indicates the text encoding to use.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>The contents of the blob, as a string.</returns>
        [DoesServiceRequest]
        public virtual async Task<string> DownloadTextAsync(Encoding encoding, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            using (SyncMemoryStream stream = new SyncMemoryStream())
            {
                await this.DownloadToStreamAsync(stream, accessCondition, options, operationContext, progressHandler, cancellationToken).ConfigureAwait(false);
                byte[] streamAsBytes = stream.ToArray();
                return (encoding ?? Encoding.UTF8).GetString(streamAsBytes, 0, streamAsBytes.Length);
            }
        }

        /// <summary>
        /// Creates a snapshot of the blob.
        /// </summary>
        /// <returns>A blob snapshot.</returns>
        [DoesServiceRequest]
        public virtual Task<CloudBlockBlob> CreateSnapshotAsync()
        {
            return this.CreateSnapshotAsync(null /* metadata */, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Creates a snapshot of the blob.
        /// </summary>
        /// <param name="metadata">A collection of name-value pairs defining the metadata of the snapshot.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">An object that specifies additional options for the request, or <c>null</c>.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A blob snapshot.</returns>
        [DoesServiceRequest]
        public virtual Task<CloudBlockBlob> CreateSnapshotAsync(IDictionary<string, string> metadata, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.CreateSnapshotAsync(metadata, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Creates a snapshot of the blob.
        /// </summary>
        /// <param name="metadata">A collection of name-value pairs defining the metadata of the snapshot.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">An object that specifies additional options for the request, or <c>null</c>.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A blob snapshot.</returns>
        [DoesServiceRequest]
        public virtual Task<CloudBlockBlob> CreateSnapshotAsync(IDictionary<string, string> metadata, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            this.attributes.AssertNoSnapshot();
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            return Executor.ExecuteAsync(
                this.CreateSnapshotImpl(metadata, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="blockData">A stream that provides the data for the block.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Stream blockData, Checksum contentChecksum)
        {
            return this.PutBlockAsync(blockId, blockData, contentChecksum, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="sourceUri">A <see cref="System.Uri"/> specifying the absolute URI to the source blob.</param>
        /// <param name="offset">The byte offset at which to begin returning content.</param>
        /// <param name="count">The number of bytes to return, or <c>null</c> to return all bytes through the end of the blob.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Uri sourceUri, long? offset, long? count, Checksum contentChecksum)
        {
            return this.PutBlockAsync(blockId, sourceUri, offset, count, contentChecksum, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="blockData">A stream that provides the data for the block.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Stream blockData, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.PutBlockAsync(blockId, blockData, contentChecksum, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="sourceUri">A <see cref="System.Uri"/> specifying the absolute URI to the source blob.</param>
        /// <param name="offset">The byte offset at which to begin returning content.</param>
        /// <param name="count">The number of bytes to return, or <c>null</c> to return all bytes through the end of the blob.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Uri sourceUri, long? offset, long? count, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.PutBlockAsync(blockId, sourceUri, offset, count, contentChecksum, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="blockData">A stream that provides the data for the block.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Stream blockData, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.PutBlockAsync(blockId, blockData, contentChecksum, accessCondition, options, operationContext, default(IProgress<StorageProgress>), cancellationToken);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="blockData">A stream that provides the data for the block.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressHandler"> A <see cref="System.IProgress{StorageProgress}"/> object to handle <see cref="StorageProgress"/> messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Stream blockData, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, IProgress<StorageProgress> progressHandler, CancellationToken cancellationToken)
        {
            return this.PutBlockAsync(blockId, blockData, contentChecksum, accessCondition, options, operationContext, new AggregatingProgressIncrementer(progressHandler), cancellationToken);
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="blockData">A stream that provides the data for the block.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="progressIncrementer"> An <see cref="AggregatingProgressIncrementer"/> object to gather progress deltas.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        private async Task PutBlockAsync(string blockId, Stream blockData, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, AggregatingProgressIncrementer progressIncrementer, CancellationToken cancellationToken)
        {
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);

            contentChecksum = contentChecksum ?? Checksum.None;

            ChecksumRequested requiresContentChecksum = new ChecksumRequested(
                md5: string.IsNullOrEmpty(contentChecksum.MD5) && modifiedOptions.ChecksumOptions.UseTransactionalMD5.Value,
                crc64: string.IsNullOrEmpty(contentChecksum.CRC64) && modifiedOptions.ChecksumOptions.UseTransactionalCRC64.Value
                );
            operationContext = operationContext ?? new OperationContext();

            ExecutionState<NullType> tempExecutionState = BlobCommonUtility.CreateTemporaryExecutionState(modifiedOptions);

            Stream blockDataAsStream = blockData;
            Stream seekableStream = blockDataAsStream;
            bool seekableStreamCreated = false;

            progressIncrementer = progressIncrementer ?? AggregatingProgressIncrementer.None;

            try
            {
                if (!blockDataAsStream.CanSeek || requiresContentChecksum.HasAny)
                {
                    Stream writeToStream;
                    if (blockDataAsStream.CanSeek)
                    {
                        writeToStream = Stream.Null;
                    }
                    else
                    {
                        seekableStream = new MultiBufferMemoryStream(this.ServiceClient.BufferManager);
                        seekableStreamCreated = true;
                        writeToStream = seekableStream;
                    }


                    StreamDescriptor streamCopyState = new StreamDescriptor();
                    long startPosition = seekableStream.Position;
                    await blockDataAsStream.WriteToAsync(writeToStream, this.ServiceClient.BufferManager, null /* copyLength */, Constants.MaxBlockSize, requiresContentChecksum, tempExecutionState, streamCopyState, cancellationToken).ConfigureAwait(false);
                    seekableStream.Position = startPosition;

                    contentChecksum = new Checksum(
                        md5: requiresContentChecksum.MD5 ? streamCopyState.Md5 : default(string),
                        crc64: requiresContentChecksum.CRC64 ? streamCopyState.Crc64 : default(string)
                        );
                }

                await Executor.ExecuteAsyncNullReturn(
                        this.PutBlockImpl(progressIncrementer.CreateProgressIncrementingStream(seekableStream), blockId, contentChecksum, accessCondition, modifiedOptions),
                        modifiedOptions.RetryPolicy,
                        operationContext,
                        cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (seekableStreamCreated)
                {
                    seekableStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Uploads a single block.
        /// </summary>
        /// <param name="blockId">A base64-encoded block ID that identifies the block.</param>
        /// <param name="sourceUri">A <see cref="System.Uri"/> specifying the absolute URI to the source blob.</param>
        /// <param name="offset">The byte offset at which to begin returning content.</param>
        /// <param name="count">The number of bytes to return, or <c>null</c> to return all bytes through the end of the blob.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockAsync(string blockId, Uri sourceUri, long? offset, long? count, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            operationContext = operationContext ?? new OperationContext();

            return Executor.ExecuteAsyncNullReturn(
                this.PutBlockImpl(sourceUri, offset, count, contentChecksum, blockId, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Uploads a list of blocks to a new or existing blob. 
        /// </summary>
        /// <param name="blockList">An enumerable collection of block IDs, as base64-encoded strings.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockListAsync(IEnumerable<string> blockList)
        {
            return this.PutBlockListAsync(blockList, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Uploads a list of blocks to a new or existing blob. 
        /// </summary>
        /// <param name="blockList">An enumerable collection of block IDs, as base64-encoded strings.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockListAsync(IEnumerable<string> blockList, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.PutBlockListAsync(blockList, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Uploads a list of blocks to a new or existing blob. 
        /// </summary>
        /// <param name="blockList">An enumerable collection of block IDs, as base64-encoded strings.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task PutBlockListAsync(IEnumerable<string> blockList, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            IEnumerable<PutBlockListItem> items = blockList.Select(i => new PutBlockListItem(i, BlockSearchMode.Latest));
            return Executor.ExecuteAsyncNullReturn(
                this.PutBlockListImpl(items, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Returns an enumerable collection of the committed blocks comprising the blob.
        /// </summary>
        /// <returns>An enumerable collection of objects implementing <see cref="ListBlockItem"/>.</returns>
        [DoesServiceRequest]
        public virtual Task<IEnumerable<ListBlockItem>> DownloadBlockListAsync()
        {
            return this.DownloadBlockListAsync(BlockListingFilter.Committed, null /* accessCondition */, null /* options */, null /* operationContext */);
        }

        /// <summary>
        /// Returns an enumerable collection of the blob's blocks, using the specified block list filter.
        /// </summary>
        /// <param name="blockListingFilter">One of the enumeration values that indicates whether to return 
        /// committed blocks, uncommitted blocks, or both.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>An enumerable collection of objects implementing <see cref="ListBlockItem"/>.</returns>
        [DoesServiceRequest]
        public virtual Task<IEnumerable<ListBlockItem>> DownloadBlockListAsync(BlockListingFilter blockListingFilter, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.DownloadBlockListAsync(blockListingFilter, accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Returns an enumerable collection of the blob's blocks, using the specified block list filter.
        /// </summary>
        /// <param name="blockListingFilter">One of the enumeration values that indicates whether to return 
        /// committed blocks, uncommitted blocks, or both.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>An enumerable collection of objects implementing <see cref="ListBlockItem"/>.</returns>
        [DoesServiceRequest]
        public virtual Task<IEnumerable<ListBlockItem>> DownloadBlockListAsync(BlockListingFilter blockListingFilter, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            return Executor.ExecuteAsync(
                this.GetBlockListImpl(blockListingFilter, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Initiates an asynchronous operation to start copying another block blob's contents, properties, and metadata to this block blob.
        /// </summary>
        /// <param name="source">A <see cref="CloudBlockBlob"/> object.</param>
        /// <returns>A <see cref="Task{T}"/> object of type <c>string</c> that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task<string> StartCopyAsync(CloudBlockBlob source)
        {
            return this.StartCopyAsync(source, default(RehydratePriority?), default(AccessCondition) /*sourceAccessCondition*/, default(AccessCondition) /*destAccessCondition*/, default(BlobRequestOptions), default(OperationContext), CancellationToken.None);
        }

        /// <summary>
        /// Initiates an asynchronous operation to start copying another block blob's contents, properties, and metadata to this block blob.
        /// </summary>
        /// <param name="source">A <see cref="CloudBlockBlob"/> object.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task{T}"/> object of type <c>string</c> that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task<string> StartCopyAsync(CloudBlockBlob source, CancellationToken cancellationToken)
        {
            return this.StartCopyAsync(source, default(RehydratePriority?), default(AccessCondition) /*sourceAccessCondition*/, default(AccessCondition) /*destAccessCondition*/, default(BlobRequestOptions), default(OperationContext), cancellationToken);
        }

        /// <summary>
        /// Initiates an asynchronous operation to start copying another block blob's contents, properties, and metadata to this block blob.
        /// </summary>
        /// <param name="source">A <see cref="CloudBlockBlob"/> object.</param>
        /// <param name="sourceAccessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the source blob. If <c>null</c>, no condition is used.</param>
        /// <param name="destAccessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the destination blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task{T}"/> object of type <c>string</c> that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task<string> StartCopyAsync(CloudBlockBlob source, AccessCondition sourceAccessCondition, AccessCondition destAccessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.StartCopyAsync(source, default(RehydratePriority?), sourceAccessCondition, destAccessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Initiates an asynchronous operation to start copying another block blob's contents, properties, and metadata to this block blob.
        /// </summary>
        /// <param name="source">A <see cref="CloudBlockBlob"/> object.</param>
        /// <param name="rehydratePriority">The priority with which to rehydrate an archived blob.</param>
        /// <param name="sourceAccessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the source blob. If <c>null</c>, no condition is used.</param>
        /// <param name="destAccessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the destination blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task{T}"/> object of type <c>string</c> that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task<string> StartCopyAsync(CloudBlockBlob source, RehydratePriority? rehydratePriority, AccessCondition sourceAccessCondition, AccessCondition destAccessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            return this.StartCopyAsync(CloudBlob.SourceBlobToUri(source), Checksum.None, false /* incrementalCopy */, false /* syncCopy */, default(PremiumPageBlobTier?),  default(StandardBlobTier?), rehydratePriority, sourceAccessCondition, destAccessCondition, options, operationContext, cancellationToken);
        }

        /// <summary>
        /// Initiates an asynchronous operation to start copying another block blob's contents, properties, and metadata to this block blob.
        /// </summary>
        /// <param name="source">The source blob.</param>    
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="incrementalCopy">A boolean indicating whether or not this is an incremental copy.</param>
        /// <param name="syncCopy">A boolean to enable synchronous server copy of blobs.</param>
        /// <param name="standardBlockBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <param name="rehydratePriority">The priority with which to rehydrate an archived blob.</param>
        /// <param name="sourceAccessCondition">An object that represents the access conditions for the source blob. If <c>null</c>, no condition is used.</param>
        /// <param name="destAccessCondition">An object that represents the access conditions for the destination blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task{T}"/> object of type <c>string</c> that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        private Task<string> StartCopyAsync(CloudBlockBlob source, Checksum contentChecksum, bool incrementalCopy, bool syncCopy, StandardBlobTier? standardBlockBlobTier, RehydratePriority? rehydratePriority, AccessCondition sourceAccessCondition, AccessCondition destAccessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            CommonUtility.AssertNotNull("source", source);
            this.attributes.AssertNoSnapshot();
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.Unspecified, this.ServiceClient);
            return Executor.ExecuteAsync(
                this.StartCopyImpl(this.attributes, CloudBlob.SourceBlobToUri(source), contentChecksum, incrementalCopy, syncCopy, default(PremiumPageBlobTier?), standardBlockBlobTier, rehydratePriority, sourceAccessCondition, destAccessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Initiates an asynchronous operation to set the tier of the blob on a standard storage account.
        /// </summary>
        /// <param name="standardBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <returns>A <see cref="Task"/> object that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task SetStandardBlobTierAsync(StandardBlobTier standardBlobTier)
        {
            return this.SetStandardBlobTierAsync(standardBlobTier, default(RehydratePriority?), default(AccessCondition), default(BlobRequestOptions), default(OperationContext), CancellationToken.None);
        }

        /// <summary>
        /// Initiates an asynchronous operation to set the tier of the blob on a standard storage account.
        /// </summary>
        /// <param name="standardBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> object that represents the asynchronous operation.</returns>
        [DoesServiceRequest]
        public virtual Task SetStandardBlobTierAsync(StandardBlobTier standardBlobTier, CancellationToken cancellationToken)
        {
            return this.SetStandardBlobTierAsync(standardBlobTier, default(RehydratePriority?), default(AccessCondition), default(BlobRequestOptions), default(OperationContext), cancellationToken);
        }

        /// <summary>
        /// Sets the tier for a blob.
        /// </summary>
        /// <param name="standardBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request, or <c>null</c>.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task SetStandardBlobTierAsync(StandardBlobTier standardBlobTier, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext)
        {
            return this.SetStandardBlobTierAsync(standardBlobTier, default(RehydratePriority?), accessCondition, options, operationContext, CancellationToken.None);
        }

        /// <summary>
        /// Sets the tier for a blob.
        /// </summary>
        /// <param name="standardBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request, or <c>null</c>.</param>
        /// <param name="operationContext">An <see cref="OperationContext"/> object that represents the context for the current operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for a task to complete.</param>
        /// <returns>A <see cref="Task"/> that represents an asynchronous action.</returns>
        [DoesServiceRequest]
        public virtual Task SetStandardBlobTierAsync(StandardBlobTier standardBlobTier, RehydratePriority? rehydratePriority, AccessCondition accessCondition, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            this.attributes.AssertNoSnapshot();
            BlobRequestOptions modifiedOptions = BlobRequestOptions.ApplyDefaults(options, BlobType.BlockBlob, this.ServiceClient);
            return Executor.ExecuteAsync(
                this.SetStandardBlobTierImpl(standardBlobTier, rehydratePriority, accessCondition, modifiedOptions),
                modifiedOptions.RetryPolicy,
                operationContext,
                cancellationToken);
        }

        /// <summary>
        /// Implementation for the CreateSnapshot method.
        /// </summary>
        /// <param name="metadata">A collection of name-value pairs defining the metadata of the snapshot, or null.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="RESTCommand"/> that creates the snapshot.</returns>
        /// <remarks>If the <c>metadata</c> parameter is <c>null</c> then no metadata is associated with the request.</remarks>
        internal RESTCommand<CloudBlockBlob> CreateSnapshotImpl(IDictionary<string, string> metadata, AccessCondition accessCondition, BlobRequestOptions options)
        {
            RESTCommand<CloudBlockBlob> putCmd = new RESTCommand<CloudBlockBlob>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) =>
            {
                StorageRequestMessage msg = BlobHttpRequestMessageFactory.Snapshot(uri, serverTimeout, accessCondition, cnt, ctx, 
                    this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials, options);
                if (metadata != null)
                {
                    BlobHttpRequestMessageFactory.AddMetadata(msg, metadata);
                }

                return msg;
            };

            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.Created, resp, null /* retVal */, cmd, ex);
                DateTimeOffset snapshotTime = NavigationHelper.ParseSnapshotTime(BlobHttpResponseParsers.GetSnapshotTime(resp));
                CloudBlockBlob snapshot = new CloudBlockBlob(this.Name, snapshotTime, this.Container);
                snapshot.attributes.Metadata = new Dictionary<string, string>(metadata ?? this.Metadata, StringComparer.OrdinalIgnoreCase);
                snapshot.attributes.Properties = new BlobProperties(this.Properties);
                CloudBlob.UpdateETagLMTLengthAndSequenceNumber(snapshot.attributes, resp, false);
                return snapshot;
            };

            return putCmd;
        }

        /// <summary>
        /// Uploads the full blob.
        /// </summary>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="SynchronousTask"/> that gets the stream.</returns>
        private RESTCommand<NullType> PutBlobImpl(Stream stream, long? length, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options)
        {
            long offset = stream.Position;
            length = length ?? stream.Length - offset;
            this.Properties.ContentChecksum = contentChecksum;

            CappedLengthReadOnlyStream cappedStream = new CappedLengthReadOnlyStream(stream, length.Value + offset);

            RESTCommand<NullType> putCmd = new RESTCommand<NullType>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildContent = (cmd, ctx) => HttpContentFactory.BuildContentFromStream(cappedStream, offset, length, Checksum.None, cmd, ctx);
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) =>
            {
                BlobRequest.VerifyHttpsCustomerProvidedKey(uri, options);
                StorageRequestMessage msg = BlobHttpRequestMessageFactory.Put(uri, serverTimeout, this.Properties, BlobType.BlockBlob, 0, null, accessCondition, cnt, ctx, 
                    this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials, options);
                BlobHttpRequestMessageFactory.AddMetadata(msg, this.Metadata);
                return msg;
            };
            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.Created, resp, NullType.Value, cmd, ex);
                CloudBlob.UpdateETagLMTLengthAndSequenceNumber(this.attributes, resp, false);

                BlobResponse.ValidateCPKHeaders(resp, options, true);
                cmd.CurrentResult.IsRequestServerEncrypted = HttpResponseParsers.ParseServerRequestEncrypted(resp);
                cmd.CurrentResult.EncryptionKeySHA256 = HttpResponseParsers.ParseEncryptionKeySHA256(resp);

                this.Properties.Length = length.Value;
                return NullType.Value;
            };

            return putCmd;
        }

        /// <summary>
        /// Uploads the block.
        /// </summary>
        /// <param name="source">The source stream.</param>
        /// <param name="blockId">The block ID.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="RESTCommand"/> that uploads the block.</returns>
        internal RESTCommand<NullType> PutBlockImpl(Stream source, string blockId, Checksum contentChecksum, AccessCondition accessCondition, BlobRequestOptions options)
        {
            long offset = source.Position;
            long length = source.Length - offset;

            RESTCommand<NullType> putCmd = new RESTCommand<NullType>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildContent = (cmd, ctx) => HttpContentFactory.BuildContentFromStream(source, offset, length, contentChecksum, cmd, ctx);
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) =>
            {
                BlobRequest.VerifyHttpsCustomerProvidedKey(uri, options);
                return BlobHttpRequestMessageFactory.PutBlock(uri, serverTimeout, blockId, accessCondition, cnt, ctx, this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials, options);
            };

            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.Created, resp, NullType.Value, cmd, ex);

                cmd.CurrentResult.IsRequestServerEncrypted = HttpResponseParsers.ParseServerRequestEncrypted(resp);
                cmd.CurrentResult.EncryptionKeySHA256 = HttpResponseParsers.ParseEncryptionKeySHA256(resp);
                BlobResponse.ValidateCPKHeaders(resp, options, true);

                return NullType.Value;
            };

            return putCmd;
        }

        /// <summary>
        /// Uploads the block from a source Uri.
        /// </summary>
        /// <param name="sourceUri">A <see cref="System.Uri"/> specifying the absolute URI to the source blob.</param>
        /// <param name="offset">The byte offset at which to begin returning content.</param>
        /// <param name="count">The number of bytes to return, or <c>null</c> to return all bytes through the end of the blob.</param>
        /// <param name="contentChecksum">An optional hash value used to ensure transactional integrity. May be <c>null</c>.</param>
        /// <param name="blockId">The block ID.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="RESTCommand"/> that uploads the block.</returns>
        internal RESTCommand<NullType> PutBlockImpl(Uri sourceUri, long? offset, long? count, Checksum contentChecksum, string blockId, AccessCondition accessCondition, BlobRequestOptions options)
        {
            RESTCommand<NullType> putCmd = new RESTCommand<NullType>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) => 
            {
                BlobRequest.VerifyHttpsCustomerProvidedKey(uri, options);
                return BlobHttpRequestMessageFactory.PutBlock(uri, sourceUri, offset, count, contentChecksum, serverTimeout, blockId, accessCondition, cnt, ctx, this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials, options);
            };
            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.Created, resp, NullType.Value, cmd, ex);
                cmd.CurrentResult.IsRequestServerEncrypted = HttpResponseParsers.ParseServerRequestEncrypted(resp);
                cmd.CurrentResult.EncryptionKeySHA256 = HttpResponseParsers.ParseEncryptionKeySHA256(resp);
                BlobResponse.ValidateCPKHeaders(resp, options, true);
                return NullType.Value;
            };

            return putCmd;
        }

        /// <summary>
        /// Uploads the block list.
        /// </summary>
        /// <param name="blocks">The blocks to upload.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="RESTCommand"/> that uploads the block list.</returns>
        internal RESTCommand<NullType> PutBlockListImpl(IEnumerable<PutBlockListItem> blocks, AccessCondition accessCondition, BlobRequestOptions options)
        {
            MultiBufferMemoryStream memoryStream = new MultiBufferMemoryStream(null /* bufferManager */, (int)(1 * Constants.KB));
            BlobRequest.WriteBlockListBody(blocks, memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            Checksum contentChecksum =
                new Checksum(
                    md5: (options.ChecksumOptions.UseTransactionalMD5.HasValue && options.ChecksumOptions.UseTransactionalMD5.Value) ? memoryStream.ComputeMD5Hash() : default(string),
                    crc64: (options.ChecksumOptions.UseTransactionalCRC64.HasValue && options.ChecksumOptions.UseTransactionalCRC64.Value) ? memoryStream.ComputeCRC64Hash() : default(string)
                    );

            RESTCommand<NullType> putCmd = new RESTCommand<NullType>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildContent = (cmd, ctx) => HttpContentFactory.BuildContentFromStream(memoryStream, 0, memoryStream.Length, contentChecksum, cmd, ctx);
            putCmd.StreamToDispose = memoryStream;
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) =>
            {
                BlobRequest.VerifyHttpsCustomerProvidedKey(uri, options);
                StorageRequestMessage msg = BlobHttpRequestMessageFactory.PutBlockList(uri, serverTimeout, this.Properties, accessCondition, cnt, ctx,
                    this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials, options);
                BlobHttpRequestMessageFactory.AddMetadata(msg, this.Metadata);
                return msg;
            };
            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.Created, resp, NullType.Value, cmd, ex);
                CloudBlob.UpdateETagLMTLengthAndSequenceNumber(this.attributes, resp, false);

                cmd.CurrentResult.IsRequestServerEncrypted = HttpResponseParsers.ParseServerRequestEncrypted(resp);
                cmd.CurrentResult.EncryptionKeySHA256 = HttpResponseParsers.ParseEncryptionKeySHA256(resp);
                BlobResponse.ValidateCPKHeaders(resp, options, true);

                this.Properties.Length = -1;
                return NullType.Value;
            };

            return putCmd;
        }

        /// <summary>
        /// Gets the download block list.
        /// </summary>
        /// <param name="typesOfBlocks">The types of blocks.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="TaskSequence"/> that gets the download block list.</returns>
        internal RESTCommand<IEnumerable<ListBlockItem>> GetBlockListImpl(BlockListingFilter typesOfBlocks, AccessCondition accessCondition, BlobRequestOptions options)
        {
            RESTCommand<IEnumerable<ListBlockItem>> getCmd = new RESTCommand<IEnumerable<ListBlockItem>>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(getCmd);
            getCmd.CommandLocationMode = CommandLocationMode.PrimaryOrSecondary;
            getCmd.RetrieveResponseStream = true;
            getCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) => BlobHttpRequestMessageFactory.GetBlockList(uri, serverTimeout, this.SnapshotTime, typesOfBlocks, accessCondition, cnt, ctx, this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials);
            getCmd.PreProcessResponse = (cmd, resp, ex, ctx) => HttpResponseParsers.ProcessExpectedStatusCodeNoException(HttpStatusCode.OK, resp, null /* retVal */, cmd, ex);
            getCmd.PostProcessResponseAsync = (cmd, resp, ctx, ct) =>
            {
                CloudBlob.UpdateETagLMTLengthAndSequenceNumber(this.attributes, resp, true);
                return GetBlockListResponse.ParseAsync(cmd.ResponseStream, ct);
            };

            return getCmd;
        }

        /// <summary>
        /// Implementation method for the SetStandardBlobTier methods.
        /// </summary>
        /// <param name="standardBlobTier">A <see cref="StandardBlobTier"/> representing the tier to set.</param>
        /// <param name="rehydratePriority">The priority with which to rehydrate an archived blob.</param>
        /// <param name="accessCondition">An <see cref="AccessCondition"/> object that represents the access conditions for the blob. If <c>null</c>, no condition is used.</param>
        /// <param name="options">A <see cref="BlobRequestOptions"/> object that specifies additional options for the request.</param>
        /// <returns>A <see cref="RESTCommand"/> that sets the blob tier.</returns>
        internal RESTCommand<NullType> SetStandardBlobTierImpl(StandardBlobTier standardBlobTier, RehydratePriority? rehydratePriority, AccessCondition accessCondition, BlobRequestOptions options)
        {
            RESTCommand<NullType> putCmd = new RESTCommand<NullType>(this.ServiceClient.Credentials, this.attributes.StorageUri, this.ServiceClient.HttpClient);

            options.ApplyToStorageCommand(putCmd);
            putCmd.BuildRequest = (cmd, uri, builder, cnt, serverTimeout, ctx) => BlobHttpRequestMessageFactory.SetBlobTier(uri, serverTimeout, standardBlobTier.ToString(), rehydratePriority, cnt, ctx, this.ServiceClient.GetCanonicalizer(), this.ServiceClient.Credentials);
            putCmd.PreProcessResponse = (cmd, resp, ex, ctx) =>
            {
                // OK is returned when the tier on the blob is done immediately while accepted occurs when the process of setting the tier has started but not completed.
                HttpStatusCode[] expectedHttpStatusCodes = new HttpStatusCode[2];
                expectedHttpStatusCodes[0] = HttpStatusCode.OK;
                expectedHttpStatusCodes[1] = HttpStatusCode.Accepted;
                HttpResponseParsers.ProcessExpectedStatusCodeNoException(expectedHttpStatusCodes, resp, NullType.Value, cmd, ex);
                CloudBlob.UpdateETagLMTLengthAndSequenceNumber(this.attributes, resp, false);

                this.attributes.Properties.RehydrationStatus = null;
                this.attributes.Properties.BlobTierInferred = false;
                if (resp.StatusCode.Equals(HttpStatusCode.OK))
                {
                    this.attributes.Properties.StandardBlobTier = standardBlobTier;
                }
                else
                {
                    this.attributes.Properties.StandardBlobTier = StandardBlobTier.Archive;
                }

                return NullType.Value;
            };

            return putCmd;
        }
    }
}
