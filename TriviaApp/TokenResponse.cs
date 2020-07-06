using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TriviaApp
{
    class TokenResponse
    {
        public int response_code { get; set; }
        public string response_message { get; set; }
        public string token { get; set; }

        public TokenResponse(int responseCode, string responseMesage, string token)
        { 
            this.response_message = responseMesage;
            this.token = token;
            this.response_code = responseCode;
        }
    }
}
