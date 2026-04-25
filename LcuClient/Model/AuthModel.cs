namespace LcuClient.Model
{
    public class AuthModel
    {
        public string Uri = "https://127.0.0.1";

        public string Port;

        public string Base64Token;

        public string GetUrl()
        {
            if (string.IsNullOrWhiteSpace(Uri) || string.IsNullOrWhiteSpace(Port))
            {
                throw new ArgumentException("Url is invalid.");
            }

            return $"{Uri}:{Port}";
        }

        public AuthModel(string port, string base64Token)
        {
            Port = port;
            Base64Token = base64Token;
        }
    }
}