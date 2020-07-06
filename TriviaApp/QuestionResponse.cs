using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TriviaApp
{
    class QuestionResponse
    {
        public int response_code { get; set; }
        public List<Question> results { get; set; }

        public QuestionResponse(int responseCode, List<Question> questions)
        {
            this.response_code = responseCode;
            this.results = questions;
        }
    }
}
