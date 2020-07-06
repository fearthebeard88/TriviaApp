using Microsoft.SqlServer.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// NOTE: To format a section use control + K control + F
// NOTE: To format the document use control + K control + D
namespace TriviaApp
{
    class Program
    {
        // TODO: Rework getting questions while players are entering categories
        // TODO: Handle API failures for async requests

        // Criteria for project:
        // Give opposing player chance to steal

        // Base URL's for each type of request
        const string BASE_URL = "https://opentdb.com/api.php";
        const string BASE_TOKEN_URL = "https://opentdb.com/api_token.php";
        const string BASE_CAT_URL = "https://opentdb.com/api_category.php";

        const int DEFAULT_ROUNDS = 10;

        // Application Response Codes
        const int APP_CODE_SUCCESS = 0;
        const int APP_CODE_NO_RES = 1;
        const int APP_CODE_INV_PARAM = 2;
        const int APP_CODE_T_NOT_FOUND = 3;
        const int APP_CODE_T_EMPTY = 4;

        /// <summary>
        /// List of dictionaries with a single value, the dictionary key is the API label, the value is
        /// the multiplier
        /// </summary>
        public static readonly List<Dictionary<string, int>> DifficultyList = new List<Dictionary<string, int>>()
        {
            new Dictionary<string, int>()
            {
                {"easy", 1}
            },
            new Dictionary<string, int>()
            {
                {"medium", 3}
            },
            new Dictionary<string, int>()
            {
                {"hard", 5}
            }
        };

        // These would be nice to use, but the API doesn't appear to have a good selection of 
        // questions for boolean types
        //const int TYPE_BOOL = 1;
        //const int TYPE_MULTI = 2;

        // token to be added to the API requests so there are no repeat questions
        public static string token;

        // dictionary to hold categories by ID
        public static Dictionary<int, Category> categories = new Dictionary<int, Category>();
        public static Dictionary<int, Category> selectedCategories = new Dictionary<int, Category>();
        public static List<Question> questions = new List<Question>();
        public static List<Task<Question>> questionTaskList = new List<Task<Question>>();
        public static int rounds;
        
        static void Main(string[] args)
        {
            // Creating token and getting categories in a non blocking (asynchronous) fashion
            Console.WriteLine("Creating token.");
            Task tokenTask = new Task(CreateToken);
            tokenTask.Start();
            
            Console.WriteLine("Getting categories");
            Task categoryTask = new Task(GetCategories);
            categoryTask.Start();

            Console.WriteLine("Greetings trivia masters!");
            List<Player> players = GetPlayers();
            Console.WriteLine($"Welcome players " +
                $"{String.Join(", ", players.Select(player => player.name).ToArray())}!\n");
            SetRounds();

            if (!tokenTask.IsCompleted)
            {
                tokenTask.Wait(TimeSpan.FromSeconds(5.0));
                if (!tokenTask.IsCompleted)
                {
                    Console.WriteLine("Timed out while waiting for token request to finish");
                    Environment.Exit(1);
                }
            }

            if (!categoryTask.IsCompleted)
            {
                categoryTask.Wait(TimeSpan.FromSeconds(5.0));
                if (!categoryTask.IsCompleted)
                {
                    Console.WriteLine("Timed out while waiting for category request to finish");
                    Environment.Exit(1);
                }
            }

            List<int> categoryIds = SelectCategories(players);

            Console.WriteLine("Getting questions...");
            GetQuestionsFromTasks();

            Console.WriteLine("Mixing questions up...");
            Random rnd = new Random();
            questions.OrderBy(q => rnd.Next());

            GetPlayerScores(players, categoryIds);
            OutputScores(players);

            Console.ReadLine();
        }

        private static void GetQuestionsFromTasks()
        {
            foreach (Task<Question> questionTask in questionTaskList)
            {
                if (!questionTask.IsCompleted && !questionTask.Wait(TimeSpan.FromSeconds(5.0)))
                {
                    Console.WriteLine("Failed to get questions from API, exiting.");
                    Environment.Exit(1);
                }

                questions.Add(questionTask.Result);
            }
        }

        private static void OutputScores(List<Player> players)
        {
            List<Player> playerList = players.OrderByDescending(p => p.totalPoints).ToList();
            List<Player> winningPlayers = GetWinningPlayers(playerList);
            Console.WriteLine($"{String.Join(", ", winningPlayers.Select(p => p.name))} are the winners! Scores below: ");
            foreach (Player player in playerList)
            {
                double percentage = (double)player.correctAnswers / player.asked;
                Console.WriteLine($"({player.name}) - Points: {player.totalPoints} " +
                    $"Correct Answers: {player.correctAnswers} Correct Percentage: {percentage:P}\n");
            }
        }

        private static List<Player> GetWinningPlayers(List<Player> playerList)
        {
            List<Player> winners = new List<Player>() { playerList[0] };
            for (int i = 1, count = playerList.Count; i < count; ++i)
            {
                if (playerList[i].totalPoints < playerList[0].totalPoints)
                {
                    break;
                }

                winners.Add(playerList[i]);
            }

            return winners;
        }

        private static void CreateToken()
        {
            TokenResponse tokenObj = CreateTokenObj();
            Debug.Assert(!String.IsNullOrWhiteSpace(tokenObj.token));
            token = tokenObj.token;
        }

        private static TokenResponse CreateTokenObj()
        {
            List<string> queryStrings = new List<string>();
            queryStrings.Add("command=request");

            string tokenResponse = MakeRequest(BASE_TOKEN_URL, queryStrings, false);
            TokenResponse tokenObj = JsonConvert.DeserializeObject<TokenResponse>(tokenResponse);

            return tokenObj;
        }

        private static void ResetToken()
        {
            Debug.Assert(!String.IsNullOrWhiteSpace(token));
            TokenResponse tokenObj = ResetTokenObj();
            Debug.Assert(!String.IsNullOrWhiteSpace(tokenObj.token));
            token = tokenObj.token;
        }

        private static TokenResponse ResetTokenObj()
        {
            List<string> queryStrings = new List<string>();
            Debug.Assert(!String.IsNullOrWhiteSpace(token));
            queryStrings.Add("command=reset");
            queryStrings.Add($"token={token}");

            string tokenResponse = MakeRequest(BASE_TOKEN_URL, queryStrings, false);
            TokenResponse tokenObj = JsonConvert.DeserializeObject<TokenResponse>(tokenResponse);

            return tokenObj;
        }

        private static void GetPlayerScores(List<Player> players, List<int> categoryIds)
        {
            Random rnd = new Random();
            int playerIndex = 0;
            for (int i = 0; i < rounds; ++i)
            {
                if (playerIndex > players.Count - 1)
                {
                    playerIndex = 0;
                }

                Player player = players[playerIndex];
                ++player.asked;
                int categoryId = categoryIds[rnd.Next(categoryIds.Count)];
                categoryIds.Remove(categoryId);

                Question question = questions[i];
                KeyValuePair<string, int> difficulty = GetDifficultyFromList(question.difficulty);

                string playerAnswer = AskQuestion(player, question);
                if (playerAnswer == question.correctAnswer)
                {
                    Console.WriteLine($"Congratulations {player.name}, {playerAnswer} is correct!");
                    player.totalPoints += 1 * difficulty.Value;
                    ++player.correctAnswers;
                    ++playerIndex;
                    continue;
                }

                Console.WriteLine($"Sorry {player.name}, {playerAnswer} was incorrect...");
                if (players.Count <= 1)
                {
                    ++playerIndex;
                    continue;
                }

                Console.WriteLine("Remaining players have a chance to steal!");
                List<Player> stealingPlayers = new List<Player>(players);
                stealingPlayers.RemoveAt(playerIndex);
                foreach (Player stealingplayer in stealingPlayers)
                {
                    playerAnswer = AskQuestion(stealingplayer, question);
                    if (playerAnswer == question.correctAnswer)
                    {
                        Console.WriteLine($"Congratulations {stealingplayer.name}, {playerAnswer} is correct!");
                        stealingplayer.totalPoints += 1 * difficulty.Value;
                        ++stealingplayer.correctAnswers;
                        break;
                    }

                    Console.WriteLine($"Sorry {stealingplayer.name}, {playerAnswer} was incorrect...");
                }

                Console.WriteLine("Next question!");
                ++playerIndex;
            }
        }

        private static string AskQuestion(Player player, Question question)
        {
            List<string> options = GetOptionsFromQuestion(question);
            Console.WriteLine($"{player.name}: {question.question}");
            Console.WriteLine($"{player.name}: Please choose your answer from the list below. (Use numbers" +
                $" next to answers)\n");
            for (int j = 0, count = options.Count; j < count; ++j)
            {
                Console.WriteLine($"({j}) - {options[j]}");
            }

            string playerAnswer = GetPlayerAnswer(options);
            return playerAnswer;
        }

        private static KeyValuePair<string, int> GetDifficultyFromList(string difficulty)
        {
            KeyValuePair<string, int> difficultyEntry = new KeyValuePair<string, int>();

            foreach (Dictionary<string, int> entry in DifficultyList)
            {
                if (entry.ContainsKey(difficulty))
                {
                    difficultyEntry = entry.First();
                    break;
                }
            }

            return difficultyEntry;
        }

        private static string GetPlayerAnswer(List<string> options)
        {
            string answer = "";
            string answerInput = Console.ReadLine().Trim();
            if (String.IsNullOrWhiteSpace(answerInput))
            {
                return answer;
            }

            int answerIndex = ConvertStringToInt(answerInput);
            if (options[answerIndex] != null)
            {
                answer = options[answerIndex];
            }

            return answer;
        }

        private static List<string> GetOptionsFromQuestion(Question question)
        {
            List<string> options = new List<string>();
            
            if (question.type == "boolean")
            {
                options.Add("True");
                options.Add("False");
                return options;
            }

            options.AddRange(question.incorrectAnswers);
            options.Add(question.correctAnswer);
            Random rnd = new Random();
            options = options.OrderBy(o => rnd.Next(options.Count)).ToList();

            return options;
        }

        private static KeyValuePair<string, int> GetDifficuty(string player)
        {
            Console.WriteLine("Difficulty affects score gained by correctly answering a question.");
            Console.WriteLine($"{player}, what difficulty would you like? (Enter the number next to it)");
            for (int i = 0, count = DifficultyList.Count; i < count; ++i)
            {
                Dictionary<string, int> difficulty = DifficultyList[i];
                KeyValuePair<string, int> labelAndMulti = difficulty.First();
                Console.WriteLine($"({i}) - {labelAndMulti.Key}: x{labelAndMulti.Value}");
            }

            // Default to easy, if the player provides a valid input we will overwrite this
            KeyValuePair<string, int> userChoice = DifficultyList[0].First();
            Console.Write($"{player}: ");
            string userInput = Console.ReadLine().Trim();
            Console.WriteLine();

            if (String.IsNullOrWhiteSpace(userInput))
            {
                // Invalid input, use the default option
                return userChoice;
            }

            int userInputNum = ConvertStringToInt(userInput);
            if (userInputNum < 0 || DifficultyList[userInputNum] != null)
            {
                userChoice = DifficultyList[userInputNum].First();
            }

            // Make sure this is a valid difficulty
            Debug.Assert(DifficultyList.Any(kvp => kvp.ContainsKey(userChoice.Key)));
            return userChoice;
        }

        private static Question GetQuestion(int categoryId, KeyValuePair<string, int> difficulty)
        {

            List<string> uriParams = new List<string>() { 
                "amount=1", 
                $"category={categoryId}",
                $"difficulty={difficulty.Key}"
            };

            string response = MakeRequest(BASE_URL, uriParams, true);
            QuestionResponse questionResponse = JsonConvert.DeserializeObject<QuestionResponse>(response);
            int responseCode = questionResponse.response_code;
            switch (responseCode)
            {
                case APP_CODE_SUCCESS:
                    // Nothing to do, the request was a success
                    break;
                case APP_CODE_T_EMPTY:
                case APP_CODE_NO_RES: // TODO: this might be the wrong place to put this
                    // The token needs to be reset and the query remade
                    Console.WriteLine("Out of questions for current session token, resetting token.");
                    ResetToken();
                    response = MakeRequest(BASE_URL, uriParams, true);
                    questionResponse = JsonConvert.DeserializeObject<QuestionResponse>(response);
                    if (questionResponse.response_code != APP_CODE_SUCCESS)
                    {
                        Console.WriteLine("Unexpectedly failed to retrieve question, exiting.");
                        Environment.Exit(1);
                    }
                    break;
                case APP_CODE_INV_PARAM:
                    Console.WriteLine("API reported an invalid parameter, exiting.");
                    Environment.Exit(1);
                    break;
                case APP_CODE_T_NOT_FOUND:
                    // Somehow we made a request without a valid token
                    Console.WriteLine("Creating token...");
                    CreateToken();
                    response = MakeRequest(BASE_URL, uriParams, true);
                    questionResponse = JsonConvert.DeserializeObject<QuestionResponse>(response);
                    if (questionResponse.response_code != APP_CODE_SUCCESS)
                    {
                        Console.WriteLine("Unexpectedly failed to retrieve question, exiting.");
                        Environment.Exit(1);
                    }
                    break;
                default:
                    Console.WriteLine("Received an unsupported application response code from API.");
                    Environment.Exit(1);
                    break;
            }

            //questions.Add(questionResponse.results[0]);
            return questionResponse.results[0];
        }

        private static void SetRounds()
        {
            Console.WriteLine("How many rounds for this game? (Leave blank to use default (10))");
            string input = Console.ReadLine().Trim();
            if (String.IsNullOrWhiteSpace(input) || !Int32.TryParse(input, out rounds))
            {
                rounds = DEFAULT_ROUNDS;
            }
        }

        public static List<int> SelectCategories(List<Player> players)
        {
            List<int> selectedCategories = new List<int>();
            Random rnd = new Random();
            GetCategories();
            Console.WriteLine("Available categories: ");
            int playerIndex = 0;
            for (int i = 0; i < rounds; i++)
            {
                if (playerIndex > players.Count - 1)
                {
                    playerIndex = 0;
                }

                Player player = players[playerIndex];
                KeyValuePair<string, int> difficulty = GetDifficuty(player.name);
                Console.WriteLine($"{player.name} pick a category (Enter the number next to it): ");
                foreach (KeyValuePair<int, Category> catEntry in categories)
                {
                    Category cat = catEntry.Value;
                    Console.WriteLine($"({cat.Id}) - {cat.Name}");
                }

                string catIdInput = Console.ReadLine().Trim();
                int catId;
                if (String.IsNullOrWhiteSpace(catIdInput) || !Int32.TryParse(catIdInput, out catId) ||
                    !categories.ContainsKey(catId))
                {
                    Console.WriteLine("Invalid category selected, picking randomly");
                    catId = categories.Keys.ToList()[rnd.Next(categories.Count)];
                }

                // Everything needed to get a question is here, we can asynchronously get the questions while the users are still inputing categories
                // Factory method for creating tasks and starting them at the same time, so we can add our tasks to the list and start them at the same time
                Task<Question> questionTask = Task<Question>.Factory.StartNew(() => GetQuestion(catId, difficulty));
                questionTaskList.Add(questionTask);

                // This list probably goes away
                selectedCategories.Add(catId);

                Console.WriteLine($"{categories[catId].Name} added to the list.\n");
                ++playerIndex;
            }

            return selectedCategories;
        }

        public static void GetCategories()
        {
            if (categories.Count > 0)
            {
                return;
            }

            String categoryResponse = MakeRequest(baseUrl:BASE_CAT_URL, useToken: false);
            var nc = JsonConvert.DeserializeObject<Dictionary<string, List<Category>>>(categoryResponse);
            List<Category> categoryList = nc["trivia_categories"];

            foreach (Category cat in categoryList)
            {
                categories.Add(cat.Id, cat);
            }
        }

        public static String MakeRequest(string baseUrl, List<string> uriParams = null, bool useToken = true)
        {
            HttpClient client = new HttpClient();
            if (uriParams == null)
            {
                // If the uriParams list is not passed in, we just create a blank list so we can add the token and perform a count operation
                uriParams = new List<string> { };
            }

            if (useToken)
            {
                Debug.Assert(!String.IsNullOrWhiteSpace(token));
                Debug.Assert(uriParams != null);
                uriParams.Add($"token={token}");
            }

            string Url = uriParams.Count <= 0 ? $"{baseUrl}" : 
                $"{baseUrl}?{String.Join("&", uriParams)}";
            Uri uri = new Uri(Url);
            var responseTask = client.GetAsync(uri);
            responseTask.Wait();
            var result = responseTask.Result;
            if (!result.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to make API request, exiting.");
                Environment.Exit(1);
            }

            var readResult = result.Content.ReadAsStringAsync();
            readResult.Wait();

            var finalResultJson = readResult.Result.ToString();
            return finalResultJson;
        }

        private static TokenResponse GetTokenObj(bool resetFlag = false, string oldToken = "")
        {
            List<string> queryStrings = new List<string>();
            if (resetFlag)
            {
                // Use an assert instead of an exception since there is no logical way for this to fail
                // and no outside inputs that would effect this method
                Debug.Assert(!String.IsNullOrWhiteSpace(oldToken));
                queryStrings.Add("command=reset");
                queryStrings.Add($"token={oldToken}");
            }
            else
            {
                queryStrings.Add("command=request");
            }

            string tokenResponse = MakeRequest(BASE_TOKEN_URL, queryStrings, false);
            TokenResponse tokenObj = JsonConvert.DeserializeObject<TokenResponse>(tokenResponse);

            return tokenObj;
        }

        public static List<Player> GetPlayers()
        {
            List<Player> players = new List<Player>();
            Console.WriteLine("Each player should enter their names below, " +
                "when you are done entering player names hit enter.");

            string name = "";
            do
            {
                Console.Write("Player Name: ");
                name = Console.ReadLine();
                if (!String.IsNullOrWhiteSpace(name))
                {
                    players.Add(new Player(name));
                }
            }
            while (!String.IsNullOrWhiteSpace(name));

            return players;
        }

        public static int ConvertStringToInt(string input)
        {
            int number;
            bool converted = Int32.TryParse(input, out number);
            if (converted == false)
            {
                Console.WriteLine("Failed to convert string to integer, exiting.");
                Environment.Exit(1);
            }

            return number;
        }
    }
}
