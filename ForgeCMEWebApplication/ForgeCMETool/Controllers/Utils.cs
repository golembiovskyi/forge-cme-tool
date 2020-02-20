namespace ForgeCMETool.Controllers
{
    public static class Utils
    {
        public static string NickName
        {
            get
            {
                return Credentials.GetAppSetting("FORGE_CLIENT_ID");
            }
        }

        public static string S3BucketName
        {
            get
            {
                return "inventor2revit" + NickName.ToLower();
            }
        }

        private static readonly char[] padding = { '=' };

        /// <summary>
        /// Base64 encode a string (source: http://stackoverflow.com/a/11743162)
        /// </summary>
        /// <param name="plainText"></param>
        /// <returns></returns>
        public static string Base64Encode(this string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes).TrimEnd(padding).Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Base64 dencode a string (source: http://stackoverflow.com/a/11743162)
        /// </summary>
        /// <param name="base64EncodedData"></param>
        /// <returns></returns>
        public static string Base64Decode(this string base64EncodedData)
        {
            string incoming = base64EncodedData.Replace('_', '/').Replace('-', '+');
            switch (base64EncodedData.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            var base64EncodedBytes = System.Convert.FromBase64String(incoming);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}