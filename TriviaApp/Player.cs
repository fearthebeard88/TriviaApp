using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TriviaApp
{
    class Player
    {
        public string name { get; set; }
        public int totalPoints { get; set; }
        public int correctAnswers { get; set; }
        public int asked { get; set; }

        public Player(string name, int points = 0, int answers = 0, int asked = 0)
        {
            this.name = name;
            this.totalPoints = points;
            this.correctAnswers = answers;
            this.asked = asked;
        }
    }
}
