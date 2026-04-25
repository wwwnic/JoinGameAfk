namespace LcuClient
{
    public class AuthModel
    {
        public string Port { get; }
        public string Base64Token { get; }

        public AuthModel(string port, string base64Token)
        {
            Port = port;
            Base64Token = base64Token;
        }
    }
}