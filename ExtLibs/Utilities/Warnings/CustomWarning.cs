﻿using System;
using System.Collections.Generic;
using System.Drawing;

namespace MissionPlanner.Warnings
{
    public class CustomWarning
    {
        public static object defaultsrc { get; set; }

        System.Reflection.PropertyInfo Item { get; set; }

        public CustomWarning Child = null;

        public enum Conditional
        {
            NONE = 0,
            LT,
            LTEQ,
            EQ,
            GT,
            GTEQ,
            NEQ
        }

        public enum WarningColors
        {
            NoColor = 0,
            Red,
            OrangeRed,
            Maroon,
            Yellow,
            Gold,
            Goldenrod,
            LawnGreen,
            Green,
            DarkGreen
        }

        // Differentiate between speak/text and QV Coloring items
        public enum WarningType
        {
            SpeakAndText = 0,
            Coloring
        }

        public CustomWarning()
        {
            RepeatTime = 10;
            Warning = 0;
            ConditionType = Conditional.NONE;
        }

        /// <summary>
        /// Return the list of options that are avaliable
        /// </summary>
        public List<string> GetOptions()
        {
            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            List<string> answer = new List<string>();

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                // field.Name has the field's name.
                object fieldValue;
                TypeCode typeCode;
                try
                {
                    fieldValue = field.GetValue(defaultsrc, null); // Get value

                    if (fieldValue == null)
                        continue;

                    // Get the TypeCode enumeration. Multiple types get mapped to a common typecode.
                    typeCode = Type.GetTypeCode(fieldValue.GetType());
                }
                catch
                {
                    continue;
                }

                answer.Add(field.Name);
            }

            return answer;
        }

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value)
                {
                    return;
                }
                _name = value;
                if (defaultsrc != null) SetField(value);
            }
        }

        string _name = "";

        /// <summary>
        /// Warning on this number based on ConditionType
        /// </summary>
        public double Warning { get; set; }

        /// <summary>
        /// used to track last time something was said
        /// </summary>
        DateTime lastrepeat;

        /// <summary>
        /// identify the type of warning  (SpeakAndText or Coloring)
        /// </summary>
        public WarningType type { get; set; }

        /// <summary>
        /// this color is used when warning fired
        /// </summary>
        public string color { get; set; }

        /// <summary>
        /// in seconds
        /// </summary>
        public int RepeatTime { get; set; }

        /// <summary>
        /// how we are checking Warning
        /// </summary>
        public Conditional ConditionType { get; set; }

        /// <summary>
        /// What we are going to say. use {warning}, {value}, {name}
        /// </summary>
        public string Text
        {
            get { return _text; }
            set { _text = value; }
        }

        string _text = "WARNING: {name} is {value}";

        /// <summary>
        /// Returns the formated string to pass to the speech engine
        /// </summary>
        /// <returns></returns>
        public string SayText()
        {
            return
                Text.Replace("{warning}", Warning.ToString("0.##"))
                    .Replace("{value}", GetValue.ToString("0.##"))
                    .Replace("{name}", Item.Name);
        }

        /// <summary>
        /// Get the current value
        /// </summary>
        public double GetValue
        {
            get
            {
                if (defaultsrc == null)
                    throw new ArgumentNullException("src");
                if (Item == null)
                    throw new ArgumentNullException("Item");

                return (double) Convert.ChangeType(Item.GetValue(defaultsrc, null), typeof (double));
            }
        }

        /// <summary>
        /// return true on match, and uses repeat time to prevent spamming
        /// </summary>
        /// <returns></returns>
        public bool CheckValue(bool userepeattime = true)
        {
            if (userepeattime && DateTime.Now < lastrepeat.AddSeconds(RepeatTime))
                return false;

            bool condition = false;

            switch (ConditionType)
            {
                case Conditional.EQ:
                    if (GetValue == Warning)
                        condition = true;
                    break;
                case Conditional.GT:
                    if (GetValue > Warning)
                        condition = true;
                    break;
                case Conditional.GTEQ:
                    if (GetValue >= Warning)
                        condition = true;
                    break;
                case Conditional.LT:
                    if (GetValue < Warning)
                        condition = true;
                    break;
                case Conditional.LTEQ:
                    if (GetValue <= Warning)
                        condition = true;
                    break;
                case Conditional.NEQ:
                    if (GetValue != Warning)
                        condition = true;
                    break;
                case Conditional.NONE:

                    break;
            }

            if (condition)
            {
                lastrepeat = DateTime.Now;
            }

            return condition;
        }


        public void SetField(string name)
        {
            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            if (name == "")
                return;

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                if (field.Name == name)
                {
                    Item = field;
                    Name = name;
                    return;
                }
            }

            throw new MissingFieldException("No such name");
        }
    }
}