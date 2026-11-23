using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Drawing;
using System.Linq;

// One row in the search results.
internal sealed class SearchItem
{
    internal string Name="",Detail="",Kind="app",Key="",Glyph="Search";
    internal string Lower="";
    internal AppPin Pin;       // set for apps, so the launcher can show the app's own icon
    internal Action Run;
    internal double Score;
}

// How a typed query is matched against a name. Every word of the query has to match, and better matches rank higher:
// the whole name, the start of the name, the start of a word, anywhere in a word, or the letters in order ("vsc" finds
// "Visual Studio Code").
internal static class Match
{
    private static readonly char[] Breaks={' ','-','_','.','(',')','/','\\'};

    // Returns a score (higher is better), or -1 when any word of the query does not match.
    internal static double Score(string[] words,string name)
    {
        if(words.Length==0)return 0;
        double total=0;
        string[] parts=name.Split(Breaks,StringSplitOptions.RemoveEmptyEntries);
        foreach(var word in words)
        {
            double best=-1;
            if(name==word)best=1000;
            else if(name.StartsWith(word,StringComparison.Ordinal))best=800;
            else
            {
                for(int i=0;i<parts.Length && best<600;i++)
                    if(parts[i].StartsWith(word,StringComparison.Ordinal))best=600-5*i;
                if(best<0)
                {
                    int index=name.IndexOf(word,StringComparison.Ordinal);
                    if(index>=0)best=400-Math.Min(150,index*2);
                }
                if(best<0 && word.Length>=2)best=Subsequence(word,name);
            }
            if(best<0)return -1;
            total+=best+Math.Max(0,40-Math.Max(0,name.Length-word.Length));   // shorter names beat longer ones
        }
        return total/words.Length;
    }

    // The letters of `word` appear in `name` in order. Scored by how tightly they sit together.
    private static double Subsequence(string word,string name)
    {
        int at=-1,gaps=0;
        foreach(char c in word)
        {
            int next=name.IndexOf(c,at+1);
            if(next<0)return -1;
            if(at>=0)gaps+=next-at-1;
            at=next;
        }
        return Math.Max(20,150-gaps*4);
    }
}

// Files, through the Windows Search index (the same one File Explorer's search box uses). It is queried on a worker
// thread, and quietly gives nothing when indexing is off or unavailable.
internal static class FileSearch
{
    internal static List<SearchItem> Query(string term,int limit)
    {
        var found=new List<SearchItem>();
        // Only letters, digits and a few separators go into the query text; the rest is dropped rather than escaped.
        var clean=new System.Text.StringBuilder();
        foreach(char c in term)if(char.IsLetterOrDigit(c)||c==' '||c=='-'||c=='_'||c=='.')clean.Append(c);
        string safe=clean.ToString().Trim();
        if(safe.Length<2)return found;
        try
        {
            using(var connection=new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
            {
                connection.Open();
                string sql="SELECT TOP "+limit+" System.ItemNameDisplay, System.ItemPathDisplay FROM SystemIndex " +
                    "WHERE System.ItemNameDisplay LIKE '%"+safe+"%' ORDER BY System.Search.Rank DESC";
                using(var command=new OleDbCommand(sql,connection))
                using(var reader=command.ExecuteReader())
                    while(reader.Read())
                    {
                        string name=reader.IsDBNull(0)?"":reader.GetString(0),path=reader.IsDBNull(1)?"":reader.GetString(1);
                        if(name.Length==0||path.Length==0)continue;
                        string target=path;
                        found.Add(new SearchItem {Name=name,Lower=name.ToLowerInvariant(),Detail=System.IO.Path.GetDirectoryName(path)??path,Kind="file",
                            Key="file:"+path,Glyph="Files",Run=()=>Shell.Open(target)});
                    }
            }
        }
        catch(Exception ex){Diag.Log("File search is unavailable: "+ex.Message);}
        return found;
    }
}
