using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace TerminalCount
{
    public class Utils
    {
        public static MySqlCommand GetDbCmd(MySqlConnection cn, CommandType cmdType, string cmdText)
        {
            cn.Open();
            MySqlCommand cmd = new MySqlCommand(cmdText, cn);
            cmd.CommandType = cmdType;
            return cmd;
        }

        public static string ConvertStringToHex(string args)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(args);
            string byteString = BitConverter.ToString(bytes).Replace("-", "");
            return byteString;
        }

        public static string ConvertHexToString(string hexString)
        {
            try
            {
                int length = hexString.Length;
                byte[] bytes = new byte[length / 2];

                for (int i = 0; i < length; i += 2)
                {
                    bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
                }

                var test9 = Encoding.BigEndianUnicode.GetChars(bytes);
                var testStr = new StringBuilder();
                foreach (var uni in test9)
                {
                    testStr.Append(uni);
                }
                return testStr.ToString();
            } catch(Exception ex)
            {
                return hexString;
            }
        }

        //public static DateTime FindDate(string str)
        //{

        //}
    }

    public class RandomNumbers
    {

        public static System.Random r;
        public RandomNumbers()
        {
        }
        public static int NextNumber()
        {
            if (r == null)
            {
                Seed();
            }

            return r.Next();
        }

        public static int NextNumber(int ceiling)
        {
            if (r == null)
            {
                Seed();
            }

            return r.Next(ceiling);
        }

        public static void Seed()
        {
            r = new System.Random();
        }

        public static void Seed(int seed)
        {
            r = new System.Random(seed);
        }
    }
}
