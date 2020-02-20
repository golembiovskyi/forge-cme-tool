using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Autodesk.Forge;
using Newtonsoft.Json.Linq;

namespace ForgeCMETool.Controllers
{
    public class UserController : ControllerBase
    {
        public Credentials Credentials { get;  set; }
        [HttpGet]
        [Route("api/forge/user/profile")]
        public async Task<JObject> GetUserProfileAsync()
        {
            if (Credentials == null)
            {
                Credentials = await Credentials.FromSessionAsync(Request.Cookies, Response.Cookies);
            }
            
            //if (Credentials == null)
            //{
            //    return null;
            //}

            // the API SDK
            UserProfileApi userApi = new UserProfileApi();
            userApi.Configuration.AccessToken = Credentials.TokenInternal;


            // get the user profile
            dynamic userProfile = await userApi.GetUserProfileAsync();

            // prepare a response with name & picture
            dynamic response = new JObject();
            response.name = string.Format("{0} {1}", userProfile.firstName, userProfile.lastName);
            response.picture = userProfile.profileImages.sizeX40;
            response.id = userProfile.userId;
            return response;
        }

    }
}