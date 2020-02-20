using Autodesk.Forge;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;


namespace ForgeCMETool.Controllers
{
    [ApiController]
    public class DesignAutomationController : ControllerBase
    {
        // Used to access the application folder (temp location for files & bundles)
        private IHostingEnvironment _env;
        // used to access the SignalR Hub
        private IHubContext<ForgeCommunicationHub> _hubContext;
        private const string ACTIVITY_NAME = "UpdateFamilyActivity";

        private string ActivityFullName { get { return string.Format("{0}.{1}+{2}", NickName, ACTIVITY_NAME, Alias); } }
        // Local folder for bundles
        public string LocalBundlesFolder { get { return Path.Combine(_env.WebRootPath, "bundles"); } }
        /// Prefix for AppBundles and Activities
        public static string NickName
        {
            get
            {
                var nickName = OAuthController.GetAppSetting("FORGE_DESIGN_AUTOMATION_NICKNAME");
                return !String.IsNullOrEmpty(nickName) ? nickName : OAuthController.GetAppSetting("FORGE_CLIENT_ID");
            }
        }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "dev"; } }
        // Design Automation v3 API
        DesignAutomationClient _designAutomation;

        // Constructor, where env and hubContext are specified
        public DesignAutomationController(IHostingEnvironment env, IHubContext<ForgeCommunicationHub> hubContext, DesignAutomationClient api)
        {
            _designAutomation = api;
            _env = env;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Names of app bundles on this project
        /// </summary>
        [HttpGet]
        [Route("api/appbundles")]
        public string[] GetLocalBundles()
        {
            // this folder is placed under the public folder, which may expose the bundles
            // but it was defined this way so it be published on most hosts easily
            return Directory.GetFiles(LocalBundlesFolder, "*.zip").Select(Path.GetFileNameWithoutExtension).ToArray();
        }

        /// <summary>
        /// Return a list of available engines
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/engines")]
        public async Task<List<string>> GetAvailableEngines()
        {
            //dynamic oauth = await OAuthController.GetInternalAsync();

            // define Engines API
            Page<string> engines = await _designAutomation.GetEnginesAsync();
            // return just REVIT engines
            return engines.Data.Where(e => e.Contains("Revit")).OrderBy(e => e).ToList<string>();
        }

        /// <summary>
        /// Define a new appbundle
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/appbundles")]
        public async Task<IActionResult> CreateAppBundle([FromBody]JObject appBundleSpecs)
        {
            // basic input validation
            string zipFileName = appBundleSpecs["zipFileName"].Value<string>();
            string engineName = appBundleSpecs["engine"].Value<string>();

            // standard name for this sample
            string appBundleName = zipFileName + "AppBundle";

            // check if ZIP with bundle is here
            string packageZipPath = Path.Combine(LocalBundlesFolder, zipFileName + ".zip");
            if (!System.IO.File.Exists(packageZipPath)) throw new Exception("Appbundle not found at " + packageZipPath);

            // get defined app bundles
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();

            // check if app bundle is already define
            dynamic newAppVersion;
            string qualifiedAppBundleId = string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias);
            if (!appBundles.Data.Contains(qualifiedAppBundleId))
            {
                // create an appbundle (version 1)
                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = appBundleName,
                    Engine = engineName,
                    Id = appBundleName,
                    Description = string.Format("Description for {0}", appBundleName),

                };
                newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");

                // create alias pointing to v1
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(appBundleName, aliasSpec);
            }
            else
            {
                // create new version
                AppBundle appBundleSpec = new AppBundle()
                {
                    Engine = engineName,
                    Description = appBundleName
                };
                newAppVersion = await _designAutomation.CreateAppBundleVersionAsync(appBundleName, appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new version");

                // update alias pointing to v+1
                AliasPatch aliasSpec = new AliasPatch()
                {
                    Version = newAppVersion.Version
                };
                Alias newAlias = await _designAutomation.ModifyAppBundleAliasAsync(appBundleName, Alias, aliasSpec);
            }

            // upload the zip with .bundle
            RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
            RestRequest request = new RestRequest(string.Empty, Method.POST);
            request.AlwaysMultipartFormData = true;
            foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
            request.AddFile("file", packageZipPath);
            request.AddHeader("Cache-Control", "no-cache");
            await uploadClient.ExecuteTaskAsync(request);

            return Ok(new { AppBundle = qualifiedAppBundleId, Version = newAppVersion.Version });
        }


        /// <summary>
        /// Helps identify the engine
        /// </summary>
        private dynamic EngineAttributes(string engine)
        {
            if (engine.Contains("3dsMax")) return new { commandLine = @"$(engine.path)\\3dsmaxbatch.exe -sceneFile $(args[inputFile].path) $(settings[script].path)", extension = "max", script = "da = dotNetClass(\"Autodesk.Forge.Sample.DesignAutomation.Max.RuntimeExecute\")\nda.ModifyWindowWidthHeight()\n" };
            if (engine.Contains("AutoCAD")) return new { commandLine = "$(engine.path)\\accoreconsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path) /s $(settings[script].path)", extension = "dwg", script = "UpdateParam\n" };
            if (engine.Contains("Inventor")) return new { commandLine = "$(engine.path)\\InventorCoreConsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path)", extension = "ipt", script = string.Empty };
            if (engine.Contains("Revit")) return new { commandLine = "$(engine.path)\\revitcoreconsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path)", extension = "rvt", script = string.Empty };
            throw new Exception("Invalid engine");
        }

        /// <summary>
        /// Define a new activity
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/activities")]
        public async Task<IActionResult> CreateActivity([FromBody]JObject activitySpecs)
        {
            // basic input validation
            string zipFileName = activitySpecs["zipFileName"].Value<string>();
            string engineName = activitySpecs["engine"].Value<string>();

            // standard name for this sample
            string appBundleName = zipFileName + "AppBundle";
            string activityName = zipFileName + "Activity";

            // 
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            string qualifiedActivityId = string.Format("{0}.{1}+{2}", NickName, activityName, Alias);
            if (!activities.Data.Contains(qualifiedActivityId))
            {
                // define the activity
                // ToDo: parametrize for different engines...
                dynamic engineAttributes = EngineAttributes(engineName);
                string commandLine = string.Format(engineAttributes.commandLine, appBundleName);
                Activity activitySpec = new Activity()
                {
                    Id = activityName,
                    Appbundles = new List<string>() { string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias) },
                    CommandLine = new List<string>() { commandLine },
                    Engine = engineName,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        //{ "inputFile", new Parameter() { Description = "input file", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        //{ "inputJson", new Parameter() { Description = "input json", LocalName = "params.json", Ondemand = false, Required = false, Verb = Verb.Get, Zip = false } },
                        //{ "outputTxt", new Parameter() { Description = "output Text file", LocalName = "result.txt", Ondemand = false, Required = false, Verb = Verb.Put, Zip = false } },
                        //{ "outputFile", new Parameter() { Description = "output model file", LocalName = "result." + engineAttributes.extension, Ondemand = false, Required = false, Verb = Verb.Put, Zip = false } }
                        { "inputFile", new Parameter() { Description = "input file", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        //{ "inputJson", new Parameter() { Description = "input json", LocalName = "params.json", Ondemand = false, Required = false, Verb = Verb.Get, Zip = false } },
                        //{ "outputTxt", new Parameter() { Description = "output Text file", LocalName = "result.txt", Ondemand = false, Required = false, Verb = Verb.Put, Zip = false } },
                        { "outputFile", new Parameter() { Description = "output model file", LocalName = "$(inputFile)", Ondemand = false, Required = false, Verb = Verb.Put, Zip = false } }
                    },
                    Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting(){ Value = engineAttributes.script } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(activityName, aliasSpec);

                return Ok(new { Activity = qualifiedActivityId });
            }

            // as this activity points to a AppBundle "dev" alias (which points to the last version of the bundle),
            // there is no need to update it (for this sample), but this may be extended for different contexts
            return Ok(new { Activity = "Activity already defined" });
        }

        /// <summary>
        /// Get all Activities defined for this account
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/activities")]
        public async Task<List<string>> GetDefinedActivities()
        {
            // filter list of 
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            List<string> definedActivities = new List<string>();
            foreach (string activity in activities.Data)
                if (activity.StartsWith(NickName) && activity.IndexOf("$LATEST") == -1)
                    definedActivities.Add(activity.Replace(NickName + ".", String.Empty));

            return definedActivities;
        }


        [HttpPost]
        [Route("api/forge/designautomation/startworkitem")]
        public async Task<IActionResult> StartWorkItem([FromForm]StartWorkitemInput input)
        //public async Task<IActionResult> StartWorkItem(string id)
        {
            // OAuth token
            //
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            string id = $"https://developer.api.autodesk.com/data/v1/projects/b.9f77180c-7cd1-40d8-9d70-d80608dfdfd9/items/urn:adsk.wipprod:dm.lineage:SXwSwlsTT_GkrOQ3GXtDUA";
            // extract the projectId & itemId from the href
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = credentials.TokenInternal;
            string[] idParams = id.Split('/');
            string itemId = idParams[idParams.Length - 1];
            string projectId = idParams[idParams.Length - 3];
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            List<int?> filterVersionNumber = new List<int?>() { 1 };
            var versions = await itemApi.GetItemVersionsAsync(projectId, itemId);

            string folderId = item.data.relationships.parent.data.id;
            string displayFileName = item.data.attributes.displayName;
            string versionId = null;
            foreach (KeyValuePair<string, dynamic> version in new DynamicDictionaryItems(versions.data))
            {
                DateTime versionDate = version.Value.attributes.lastModifiedTime;
                string verNum = version.Value.id.Split("=")[1];
                string userName = version.Value.attributes.lastModifiedUserName;
                versionId = version.Value.id;
                string urn = string.Empty;
                try { urn = (string)version.Value.relationships.derivatives.data.id; }
                catch { }
            }
            //

            // basic input validation
            JObject workItemData = JObject.Parse(input.data);
            string widthParam = workItemData["width"].Value<string>();
            string heigthParam = workItemData["height"].Value<string>();
            string activityName = string.Format("{0}.{1}", NickName, workItemData["activityName"].Value<string>());
            string browerConnectionId = workItemData["browerConnectionId"].Value<string>();

            // save the file on the server
            var fileSavePath = Path.Combine(_env.ContentRootPath, Path.GetFileName(input.inputFile.FileName));
            using (var stream = new FileStream(fileSavePath, FileMode.Create)) await input.inputFile.CopyToAsync(stream);



            // upload file to OSS Bucket
            // 1. ensure bucket existis
            string bucketKey = NickName.ToLower() + "_designautomation";
            BucketsApi buckets = new BucketsApi();
            buckets.Configuration.AccessToken = credentials.TokenInternal;
            try
            {
                PostBucketsPayload bucketPayload = new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }; // in case bucket already exists
                       // 2. upload inputFile
            string inputFileNameOSS = string.Format("{0}_input_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(input.inputFile.FileName)); // avoid overriding
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = credentials.TokenInternal;
            using (StreamReader streamReader = new StreamReader(fileSavePath))
                await objects.UploadObjectAsync(bucketKey, inputFileNameOSS, (int)streamReader.BaseStream.Length, streamReader.BaseStream, "application/octet-stream");
            System.IO.File.Delete(fileSavePath);// delete server copy

            // prepare workitem arguments
            // 1. input file
            XrefTreeArgument inputFileArgument = new XrefTreeArgument()
            {
                Url = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, inputFileNameOSS),
                Headers = new Dictionary<string, string>()
                 {
                     { "Authorization", "Bearer " + credentials.TokenInternal }
                 }
            };
            //XrefTreeArgument inputFileArgument = BuildBIM360DownloadURL(oauth.access_token, projectId, versionId);
            // 2. input json
            dynamic inputJson = new JObject();
            inputJson.Width = widthParam;
            inputJson.Height = heigthParam;
            XrefTreeArgument inputJsonArgument = new XrefTreeArgument()
            {
                Url = "data:application/json, " + ((JObject)inputJson).ToString(Formatting.None).Replace("\"", "'")
            };
            // 3. output file
            string outputFileNameOSS = string.Format("{0}_output_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(input.inputFile.FileName)); // avoid overriding
            XrefTreeArgument outputFileArgument = new XrefTreeArgument()
            {
                Url = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, outputFileNameOSS),
                Verb = Verb.Put,
                Headers = new Dictionary<string, string>()
                   {
                       {"Authorization", "Bearer " + credentials.TokenInternal }
                   }
            };

            // prepare & submit workitem
            string callbackUrl = string.Format("{0}/api/forge/callback/designautomation?id={1}&outputFileName={2}", OAuthController.GetAppSetting("FORGE_WEBHOOK_URL"), browerConnectionId, outputFileNameOSS);
            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = activityName,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "inputFile", inputFileArgument },
                    { "inputJson",  inputJsonArgument },
                    { "outputFile", outputFileArgument },
                    { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemsAsync(workItemSpec);

            return Ok(new { WorkItemId = workItemStatus.Id });
        }


        /// <summary>
        /// Callback from Design Automation Workitem (onProgress or onComplete)
        /// </summary>
        [HttpPost]
        [Route("/api/forge/callback/designautomation")]
        public async Task<IActionResult> OnCallback(string id, string bucketKey, string outputFileName, [FromBody]dynamic body)
        {
            try
            {
                // your webhook should return immediately! we can use Hangfire to schedule a job
                JObject bodyJson = JObject.Parse((string)body.ToString());
                await _hubContext.Clients.Client(id).SendAsync("onComplete", bodyJson.ToString());

                var client = new RestClient(bodyJson["reportUrl"].Value<string>());
                var request = new RestRequest(string.Empty);

                // send the result output log to the client
                byte[] bs = client.DownloadData(request);
                string report = System.Text.Encoding.Default.GetString(bs);
                await _hubContext.Clients.Client(id).SendAsync("onComplete", report);

                // generate a signed URL to download the result file and send to the client
                ObjectsApi objectsApi = new ObjectsApi();
                dynamic signedUrl = await objectsApi.CreateSignedResourceAsyncWithHttpInfo(bucketKey, outputFileName, new PostBucketsSigned(10), "read");
                string signedUrlLink = signedUrl.Data.signedUrl;
                // send the json content to client if result is text, for countitactivity
                if (Path.GetExtension(outputFileName) == ".txt")
                {
                    // get the content of the result file
                    client = new RestClient(signedUrlLink);
                    byte[] file = client.DownloadData(request);
                    string result = System.Text.Encoding.Default.GetString(file);
                    await _hubContext.Clients.Client(id).SendAsync("countItResult", result);
                }

                await _hubContext.Clients.Client(id).SendAsync("downloadResult", signedUrlLink);
            }
            catch { }

            // ALWAYS return ok (200)
            return Ok();
        }


        /// <summary>
        /// Clear the accounts (for debugging purpouses)
        /// </summary>
        [HttpDelete]
        [Route("api/forge/designautomation/account")]
        public async Task<IActionResult> ClearAccount()
        {
            // clear account
            await _designAutomation.DeleteForgeAppAsync("me");
            return Ok();
        }

        /// <summary>
        /// Input for StartWorkitem
        /// </summary>
        public class StartWorkitemInput
        {
            public IFormFile inputFile { get; set; }
            public string data { get; set; }
        }

        [HttpGet]
        [Route("api/forge/designautomation/testing1")]
        //public async Task<IActionResult> Testing(string id)
        public async Task<IList<jsTreeNode>> Testing1(string id)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();
            // the API SDK
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = credentials.TokenInternal;

            // extract the projectId & itemId from the href
            string[] idParams = id.Split('/');
            string itemId = idParams[idParams.Length - 1];
            string projectId = idParams[idParams.Length - 3];

            var versions = await itemApi.GetItemVersionsAsync(projectId, itemId);
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            string folderId = item.data.relationships.parent.data.id;
            string fileName = item.data.attributes.displayName;
            string versionId = null;
            foreach (KeyValuePair<string, dynamic> version in new DynamicDictionaryItems(versions.data))
            {
                DateTime versionDate = version.Value.attributes.lastModifiedTime;
                string verNum = version.Value.id.Split("=")[1];
                string userName = version.Value.attributes.lastModifiedUserName;
                versionId = version.Value.id;
                string urn = string.Empty;
                try { urn = (string)version.Value.relationships.derivatives.data.id; }
                catch { }
            }
            // Prepare the DA input from BIM 360
            var input = await BuildBIM360DownloadURL(credentials.TokenInternal, projectId, versionId);
            //var output = await PreWorkNewVersion(credentials.TokenInternal, projectId, versionId);
            // Create a version for this new file
            // prepare storage
            ProjectsApi projectApis = new ProjectsApi();
            projectApis.Configuration.AccessToken = credentials.TokenInternal;
            StorageRelationshipsTargetData storageRelData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
            CreateStorageDataRelationshipsTarget storageTarget = new CreateStorageDataRelationshipsTarget(storageRelData);
            CreateStorageDataRelationships storageRel = new CreateStorageDataRelationships(storageTarget);
            BaseAttributesExtensionObject attributes = new BaseAttributesExtensionObject(string.Empty, string.Empty, new JsonApiLink(string.Empty), null);
            CreateStorageDataAttributes storageAtt = new CreateStorageDataAttributes(fileName, attributes);
            CreateStorageData storageData = new CreateStorageData(CreateStorageData.TypeEnum.Objects, storageAtt, storageRel);
            CreateStorage storage = new CreateStorage(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), storageData);
            dynamic storageCreated = await projectApis.PostStorageAsync(projectId, storage);

            VersionsApi versionsApi = new VersionsApi();
            versionsApi.Configuration.AccessToken = credentials.TokenInternal;
            CreateVersion newVersionData = new CreateVersion
                (
                new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0),
                new CreateVersionData
                (
                    CreateVersionData.TypeEnum.Versions,
                    new CreateStorageDataAttributes
                    (
                        fileName,
                        new BaseAttributesExtensionObject
                        (
                            "versions:autodesk.bim360:File",
                            "1.0",
                            new JsonApiLink(string.Empty),
                            null
                        )
                   ),
                    new CreateVersionDataRelationships
                             (
                                new CreateVersionDataRelationshipsItem
                                (
                                  new CreateVersionDataRelationshipsItemData
                                  (
                                    CreateVersionDataRelationshipsItemData.TypeEnum.Items,
                                    itemId
                                  )
                                ),
                                new CreateItemRelationshipsStorage
                                (
                                  new CreateItemRelationshipsStorageData
                                  (
                                    CreateItemRelationshipsStorageData.TypeEnum.Objects,
                                    storageCreated.data.id
                                  )
                                )
                             )
                           )
                        );
            dynamic newVersion = await versionsApi.PostVersionAsync(projectId, newVersionData);
            return nodes;
        }

        [HttpGet]
        [Route("api/forge/designautomation/testing")]
        //public async Task<IActionResult> Testing(string id)
        public async Task<IList<jsTreeNode>> Testing(string id)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();
            // the API SDK
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = credentials.TokenInternal;

            // extract the projectId & itemId from the href
            string[] idParams = id.Split('/');
            string itemId = idParams[idParams.Length - 1];
            string projectId = idParams[idParams.Length - 3];

            var versions = await itemApi.GetItemVersionsAsync(projectId, itemId);
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            string folderId = item.data.relationships.parent.data.id;
            string displayFileName = item.data.attributes.displayName;
            string versionId = null;
            foreach (KeyValuePair<string, dynamic> version in new DynamicDictionaryItems(versions.data))
            {
                DateTime versionDate = version.Value.attributes.lastModifiedTime;
                string verNum = version.Value.id.Split("=")[1];
                string userName = version.Value.attributes.lastModifiedUserName;
                versionId = version.Value.id;
                if (verNum=="1")
                {
                    break;
                }
                string urn = string.Empty;
                try { urn = (string)version.Value.relationships.derivatives.data.id; }
                catch { }
            }
            //get user id
            UserController user = new UserController();
            user.Credentials = credentials;
            dynamic userProfile = await user.GetUserProfileAsync();
            string userId = userProfile.id;
            //// Prepare the DA input from BIM 360
            //var input = await BuildBIM360DownloadURL(credentials.TokenInternal, projectId, versionId);
            //// Prepare the DA output to BIM 360
            //var storageInfo = await PreWorkNewVersion(credentials.TokenInternal, projectId, versionId);
            //string storageId = storageInfo.storageId;
            //string fileName = storageInfo.fileName;
            //try
            //{
            //    BackgroundJob.Schedule(() => PostProcessFile(credentials.TokenInternal, userId, projectId, itemId, storageId, fileName), TimeSpan.FromSeconds(1));
            //}
            //catch (Exception e) { }
            //
            StorageInfo info = await PreWorkNewVersion(credentials.TokenInternal, projectId, versionId);
            string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/revit/{1}/{2}/{3}/{4}/{5}", Credentials.GetAppSetting("FORGE_WEBHOOK_URL"), userId, projectId, info.itemId.Base64Encode(), info.storageId.Base64Encode(), info.fileName.Base64Encode());

            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = ActivityFullName,
                Arguments = new Dictionary<string, IArgument>()
                    {
                        { "inputFile", await BuildBIM360DownloadURL(credentials.TokenInternal, projectId, versionId) },
                        { "outputFile",  await BuildBIM360UploadURL(credentials.TokenInternal, info)  },
                        { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                    }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemsAsync(workItemSpec);
            try
            {
                var storageInfo = await PreWorkNewVersion(credentials.TokenInternal, projectId, versionId);
                string storageId = storageInfo.storageId;
                string fileName = storageInfo.fileName;
                BackgroundJob.Schedule(() => PostProcessFile(credentials.TokenInternal, userId, projectId, itemId, storageId, fileName), TimeSpan.FromSeconds(1));
            }
            catch (Exception e) { }

            //
            return nodes;
        }
        /// <summary>
        /// Prepare the DA input from BIM 360
        /// </summary>
        private async Task<XrefTreeArgument> BuildBIM360DownloadURL(string userAccessToken, string projectId, string versionId)
        {
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic version = await versionApi.GetVersionAsync(projectId, versionId);
            //dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

            string[] versionItemParams = ((string)version.data.relationships.storage.data.id).Split('/');
            string[] bucketKeyParams = versionItemParams[versionItemParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = versionItemParams[versionItemParams.Length - 1];
            string downloadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

            return new XrefTreeArgument()
            {
                Url = downloadUrl,
                Verb = Verb.Get,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + userAccessToken }
                }
            };
        }

        /// <summary>
        /// Prepare the DA output to BIM 360
        /// </summary>

        private async Task<dynamic> PreWorkNewVersion(string userAccessToken, string projectId, string versionId)
        {
            // get version
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

            // get item
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = userAccessToken;
            string itemId = versionItem.data.id;
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            string folderId = item.data.relationships.parent.data.id;
            string fileName = item.data.attributes.displayName;

            // prepare storage
            ProjectsApi projectApi = new ProjectsApi();
            projectApi.Configuration.AccessToken = userAccessToken;
            StorageRelationshipsTargetData storageRelData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
            CreateStorageDataRelationshipsTarget storageTarget = new CreateStorageDataRelationshipsTarget(storageRelData);
            CreateStorageDataRelationships storageRel = new CreateStorageDataRelationships(storageTarget);
            BaseAttributesExtensionObject attributes = new BaseAttributesExtensionObject(string.Empty, string.Empty, new JsonApiLink(string.Empty), null);
            CreateStorageDataAttributes storageAtt = new CreateStorageDataAttributes(fileName, attributes);
            CreateStorageData storageData = new CreateStorageData(CreateStorageData.TypeEnum.Objects, storageAtt, storageRel);
            CreateStorage storage = new CreateStorage(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), storageData);
            dynamic storageCreated = await projectApi.PostStorageAsync(projectId, storage);

            string[] storageIdParams = ((string)storageCreated.data.id).Split('/');
            string[] bucketKeyParams = storageIdParams[storageIdParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = storageIdParams[storageIdParams.Length - 1];

            string uploadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

            return new StorageInfo
            {
                fileName = fileName,
                itemId = item.data.id,
                storageId = storageCreated.data.id,
                uploadUrl = uploadUrl
            };
        }

        //
        private async Task<XrefTreeArgument> BuildBIM360UploadURL(string userAccessToken, StorageInfo info)
        {
            return new XrefTreeArgument()
            {
                Url = info.uploadUrl,
                Verb = Verb.Put,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + userAccessToken }
                }
            };
        }

        /// <summary>
        /// After the DA is done and the output file was saved into BIM 360, you need to mark that as a new version 
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/postprocessfile")]
        public async Task PostProcessFile(string userAccessToken, string userId, string projectId, string itemId, string storageId, string fileName)
        {
            //Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            VersionsApi versionsApis = new VersionsApi();
            versionsApis.Configuration.AccessToken = userAccessToken;
            CreateVersion newVersionData = new CreateVersion
            (
               new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0),
               new CreateVersionData
               (
                 CreateVersionData.TypeEnum.Versions,
                 new CreateStorageDataAttributes
                 (
                   fileName,
                   new BaseAttributesExtensionObject
                   (
                     "versions:autodesk.bim360:File",
                     "1.0",
                     new JsonApiLink(string.Empty),
                     null
                   )
                 ),
                 new CreateVersionDataRelationships
                 (
                    new CreateVersionDataRelationshipsItem
                    (
                      new CreateVersionDataRelationshipsItemData
                      (
                        CreateVersionDataRelationshipsItemData.TypeEnum.Items,
                        itemId
                      )
                    ),
                    new CreateItemRelationshipsStorage
                    (
                      new CreateItemRelationshipsStorageData
                      (
                        CreateItemRelationshipsStorageData.TypeEnum.Objects,
                        storageId
                      )
                    )
                 )
               )
            );
            dynamic newVersion = await versionsApis.PostVersionAsync(projectId, newVersionData);
        }

        private struct StorageInfo
        {
            public string fileName;
            public string itemId;
            public string storageId;
            public string uploadUrl;
        }

    }
    /// <summary>
    /// Class uses for SignalR
    /// </summary>
    public class ForgeCommunicationHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string GetConnectionId() { return Context.ConnectionId; }
    }

}