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

        //public static DateTime FindDate(string str)
        //{

        //}
    }
}
