using System;
using System.IO;
using EngageRuntime;
using EAX;


namespace EAXTester
{
    class Program
    {
        EAX.Parser parser;
        
        static void Main(string[] args)
        {
            Program program = new Program();
            program.TestParser();

        }

        public void TestParser()
        {
            string path = Path.Combine("..", "..", "..", "TestFile.xml");
            string input = System.IO.File.ReadAllText(path);
            //Console.WriteLine(input);
            parser = new Parser("<input> <info> Contents </info> </input>");
            var result = parser.Parse() as EAX.EngagedXmlDoc;
            foreach (EAX.TagOpen tagOpen in result.tags)
            {
                Console.WriteLine(tagOpen);
            }
        }
    }
}