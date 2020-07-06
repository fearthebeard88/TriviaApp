using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TriviaApp
{
    class Question
    {
        public string category { get; set; }
        public string type { get; set; }
        public string difficulty { get; set; }
        public string question { get; set; }
        public string correctAnswer { get; set; }
        public string[] incorrectAnswers { get; set; }

        public Question(string category, string type, string difficulty, string question, string correct_answer, string[] incorrect_answers)
        {
            this.category = WebUtility.HtmlDecode(category);
            this.type = WebUtility.HtmlDecode(type);
            this.difficulty = WebUtility.HtmlDecode(difficulty);
            this.question = WebUtility.HtmlDecode(question);
            this.correctAnswer = WebUtility.HtmlDecode(correct_answer);

            string[] options = new string[incorrect_answers.Length];
            for (int i = 0, count = incorrect_answers.Length; i < count; ++i)
            {
                string option = incorrect_answers[i];
                string decodedOption = WebUtility.HtmlDecode(option);
                options[i] = decodedOption;
            }

            this.incorrectAnswers = options;
        }
    }
}
