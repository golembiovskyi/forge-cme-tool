﻿using Autodesk.Forge;
using Autodesk.Forge.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ForgeCMETool.Controllers
{
    [ApiController]
    public class OSSController : ControllerBase
    {
        public static string NickName { get { return OAuthController.GetAppSetting("FORGE_CLIENT_ID"); } }
        /// <summary>
        /// Return list of buckets (id=#) or list of objects (id=bucketKey)
        /// </summary>
        [HttpGet]
        [Route("api/forge/oss/buckets")]
        public async Task<IList<TreeNode>> GetOSSAsync([FromQuery]string id)
        {
            IList<TreeNode> nodes = new List<TreeNode>();
            //dynamic oauth = await OAuthController.GetInternalAsync();
            //
            //
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            //
            //string bucketKey = NickName.ToLower() + "_designautomation";
            //id = "#";
            //

            if (id == "#") // root
            {
                // in this case, let's return all buckets
                BucketsApi appBckets = new BucketsApi();
                appBckets.Configuration.AccessToken = credentials.TokenInternal;
                //appBckets.Configuration.AccessToken = oauth.access_token;

                // to simplify, let's return only the first 100 buckets
                dynamic buckets = await appBckets.GetBucketsAsync("US", 100);
                foreach (KeyValuePair<string, dynamic> bucket in new DynamicDictionaryItems(buckets.items))
                {
                    nodes.Add(new TreeNode(bucket.Value.bucketKey, bucket.Value.bucketKey, "bucket", true));
                }
            }
            else
            {
                // as we have the id (bucketKey), let's return all 
                ObjectsApi objects = new ObjectsApi();
                //objects.Configuration.AccessToken = oauth.access_token;
                objects.Configuration.AccessToken = credentials.TokenInternal;
                var objectsList = objects.GetObjects(id, 100);
                foreach (KeyValuePair<string, dynamic> objInfo in new DynamicDictionaryItems(objectsList.items))
                {
                    nodes.Add(new TreeNode(Base64Encode((string)objInfo.Value.objectId),
                      objInfo.Value.objectKey, "object", false));
                }
            }
            return nodes;
        }

        /// <summary>
        /// Model data for jsTree used on GetOSSAsync
        /// </summary>
        public class TreeNode
        {
            public TreeNode(string id, string text, string type, bool children)
            {
                this.id = id;
                this.text = text;
                this.type = type;
                this.children = children;
            }

            public string id { get; set; }
            public string text { get; set; }
            public string type { get; set; }
            public bool children { get; set; }
        }

        /// <summary>
        /// Create a new bucket 
        /// </summary>
        [HttpPost]
        [Route("api/forge/oss/buckets")]
        public async Task<dynamic> CreateBucket([FromBody]CreateBucketModel bucket)
        {
            BucketsApi buckets = new BucketsApi();
            //
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            //
            //dynamic token = await OAuthController.GetInternalAsync();
            buckets.Configuration.AccessToken = credentials.TokenInternal;
            PostBucketsPayload bucketPayload = new PostBucketsPayload(bucket.bucketKey, null,
              PostBucketsPayload.PolicyKeyEnum.Transient);
            return await buckets.CreateBucketAsync(bucketPayload, "US");
        }

        /// <summary>
        /// Input model for CreateBucket method
        /// </summary>
        public class CreateBucketModel
        {
            public string bucketKey { get; set; }
        }

        /// <summary>
        /// Receive a file from the client and upload to the bucket
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("api/forge/oss/objects")]
        public async Task<dynamic> UploadObject([FromForm]UploadFileInput input)
        {
            // basic input validation
            if (string.IsNullOrWhiteSpace(input.bucketKey))
                throw new System.Exception("BucketKey parameter was not provided.");

            if (input.inputFile == null )
                throw new System.Exception("Missing file to upload"); // for now, let's support just 1 file at a time

            string bucketKey = input.bucketKey;

            var fileSavePath = Path.Combine( Directory.GetCurrentDirectory()+"\\wwwroot\\appdata", Path.GetFileName(input.inputFile.FileName));
            using (var stream = new FileStream(fileSavePath, FileMode.Create))
                await input.inputFile.CopyToAsync(stream);
            //
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            //

            // get the bucket...
            //dynamic oauth = await OAuthController.GetInternalAsync();
            ObjectsApi objects = new ObjectsApi();
            //objects.Configuration.AccessToken = oauth.access_token;
            objects.Configuration.AccessToken = credentials.TokenInternal;

            // upload the file/object, which will create a new object
            dynamic uploadedObj;
            using (StreamReader streamReader = new StreamReader(fileSavePath))
            {
                uploadedObj = await objects.UploadObjectAsync(bucketKey,
                       Path.GetFileName(input.inputFile.FileName), (int)streamReader.BaseStream.Length, streamReader.BaseStream,
                       "application/octet-stream");
            }

            // cleanup
            System.IO.File.Delete(fileSavePath);// delete server copy

            return uploadedObj;
        }


        public class UploadFileInput
        {
            public IFormFile inputFile { get; set; }
            public string bucketKey { get; set; }
        }

        /// <summary>
        /// Base64 enconde a string
        /// </summary>
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }
    }
}

