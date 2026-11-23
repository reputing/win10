using System;
using System.Globalization;

// A small calculator for the search box: numbers, + - * / % ^, brackets, and x or × for times. It is a hand-written
// parser, so nothing a person types is ever executed as code.
internal static class Calc
{
    private sealed class Reader
    {
        private readonly string text;
        private int at;
        internal Reader(string text){this.text=text;}
        internal bool Done { get { Skip(); return at>=text.Length; } }
        private void Skip(){while(at<text.Length && text[at]==' ')at++;}
        private bool Take(char c){Skip();if(at<text.Length && text[at]==c){at++;return true;}return false;}

        // sum := product (('+'|'-') product)*
        internal double Sum()
        {
            double value=Product();
            while(true)
            {
                if(Take('+'))value+=Product();
                else if(Take('-'))value-=Product();
                else return value;
            }
        }
        private double Product()
        {
            double value=Power();
            while(true)
            {
                if(Take('*')||Take('x')||Take('×'))value*=Power();
                else if(Take('/')||Take('÷'))
                {
                    double divisor=Power();
                    if(divisor==0)throw new FormatException("divide by zero");
                    value/=divisor;
                }
                else if(Take('%'))
                {
                    double divisor=Power();
                    if(divisor==0)throw new FormatException("divide by zero");
                    value%=divisor;
                }
                else return value;
            }
        }
        private double Power()
        {
            double value=Unary();
            if(Take('^')){double exponent=Power();return Math.Pow(value,exponent);}
            return value;
        }
        private double Unary()
        {
            if(Take('-'))return -Unary();
            if(Take('+'))return Unary();
            if(Take('('))
            {
                double inner=Sum();
                if(!Take(')'))throw new FormatException("missing bracket");
                return inner;
            }
            Skip();
            int start=at;
            while(at<text.Length && (char.IsDigit(text[at]) || text[at]=='.' || text[at]==','))at++;
            double number;
            if(at==start || !double.TryParse(text.Substring(start,at-start).Replace(",",""),NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                throw new FormatException("number expected");
            return number;
        }
    }

    // The answer as text, or null when the input is not arithmetic (so ordinary searches are never mistaken for sums).
    internal static string Evaluate(string input)
    {
        if(input==null || input.Length>80)return null;
        bool digit=false,operatorSeen=false;
        foreach(char c in input)
        {
            if(char.IsDigit(c))digit=true;
            else if("+-*/%^x×÷".IndexOf(c)>=0)operatorSeen=true;
            else if(c!=' ' && c!='.' && c!=',' && c!='(' && c!=')')return null;
        }
        if(!digit || !operatorSeen)return null;
        try
        {
            var reader=new Reader(input.Trim());
            double value=reader.Sum();
            if(!reader.Done || double.IsNaN(value) || double.IsInfinity(value))return null;
            return value.ToString("0.##########",CultureInfo.InvariantCulture);
        }
        catch(FormatException){return null;}
    }
}
