using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

class Program
{

    static IEnumerable<string> GetFiles(string path)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0)
        {
            path = queue.Dequeue();
            try
            {
                foreach (string subDir in Directory.GetDirectories(path))
                {
                    queue.Enqueue(subDir);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            string[] files = null;
            try
            {
                files = Directory.GetFiles(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    yield return files[i];
                }
            }
        }
    }

    static List<string> FetchIbanRegex(string filePath)
    {
        var regex = new Regex(@"^.*([A-Z]{2}[ \-]?[0-9]{2}(?=(?:[ \-]?[A-Z0-9]){9,30})(?:[ \-]?[A-Z0-9]{3,9}){2,7}[ \-]?[A-Z0-9]{1,3}?).*$");
        var data = File.ReadAllLinesAsync(filePath).Result;
        var list = new List<string>();
        foreach(var l in data)
        {
            if(regex.IsMatch(l))
            {
                list.Add(regex.Match(l).Groups[1].Value);
            }
        }
        return list;
    }

    static string outputdir = string.Empty;

    public static string Parse(
        string result,
        string iban,
        string check1,
        string check2,
        string check3,
        string check4
        )
    {
        var honeyPotResult = new HoneyPotResult(
          result,
          iban,
          check1,
          check2,
          check3,
          check4
        );
        var jsonResult = string.Empty;
        try
        {
            jsonResult = JsonConvert.SerializeObject(honeyPotResult, Converter.Settings);
        }
        catch (Exception ex)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine("Critical error\n" + ex.Message);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        return jsonResult;
    }

    static async Task Scrape(List<string> ibans)
    {
        Console.WriteLine("iban fetching completed");
        Console.WriteLine("scraping...");
        using var client = new HttpClient();
        foreach (var iban in ibans)
        {
            string scraped_data = null;
            try
            {
                Console.WriteLine(iban);
                scraped_data = client.GetStringAsync("https://www.ibancalculator.com/iban_validieren.html?&tx_valIBAN_pi1%5Bfi%5D=fi&tx_valIBAN_pi1%5Biban%5D=" + iban).Result;
            }
            catch
            {
                Console.WriteLine("failed request; trying again, config: 20 attempts 5000ms cooldown");
                var dodger = 0;
                while (scraped_data != null && dodger < 20)
                {
                    try
                    {
                        dodger++;
                        scraped_data = await client.GetStringAsync("https://www.ibancalculator.com/iban_validieren.html?&tx_valIBAN_pi1%5Bfi%5D=fi&tx_valIBAN_pi1%5Biban%5D=" + iban);
                    }
                    catch { }
                    Thread.Sleep(5000);
                }
            }
            var result_regex = new Regex(@"<fieldset><legend>Result<\/legend><p><b>(.*)<\/b><\/p>");
            var checks_regex = new Regex(
                @"<td valign=""top""><img src=""data:image\/gif;base64,(?:[A-Za-z0-9+\/]{4})*(?:[A-Za-z0-9+\/]{2}==|[A-Za-z0-9+\/]{3}=)"" alt=""\+""><\/td><td><p>([a-zA-Z0-9_.\- :()]*)"
            );

            if(result_regex.IsMatch(scraped_data))
            {
                var result = result_regex.Match(scraped_data).Groups[1].Value;
                var matches = checks_regex.Matches(scraped_data);

                var json = Parse(result, iban,
                    matches[0].Groups[1].Value,
                    matches[1].Groups[1].Value,
                    matches[2].Groups[1].Value,
                    matches[3].Groups[1].Value
                );

                if (!File.Exists(outputdir))
                {
                    await File.WriteAllTextAsync(outputdir, json);
                }
                else
                {
                    await File.WriteAllTextAsync(outputdir, File.ReadAllTextAsync(outputdir).Result.Replace("}}", "},\n")+json.Replace("{\"resultClass\":", "\"resultClass\":"));
                }
            }
            Thread.Sleep(1500);
        }
    }

    static async Task Main()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Folder/File path: ");
        Console.ForegroundColor = ConsoleColor.White;
        var path = Console.ReadLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Output file path: ");
        Console.ForegroundColor = ConsoleColor.White;
        outputdir = Console.ReadLine();
        var list = new List<string>();
        if (Directory.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Use recursive search? ([y/n]): ");
            Console.ForegroundColor = ConsoleColor.White;
            var yn = Console.ReadLine();
            if (yn.Contains("y"))
            {
                foreach (string file in GetFiles(path))
                {
                    list.AddRange(FetchIbanRegex(file));
                }
            }
            else
            {
                foreach(string file in Directory.GetFiles(path))
                {
                    list.AddRange(FetchIbanRegex(file));
                }
            }
            await Scrape(list);
        }
        else if (File.Exists(path))
        {
            list.AddRange(FetchIbanRegex(path));
            await Scrape(list);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Wrong file or folder path, press any key to try again.");
            Console.ForegroundColor = ConsoleColor.White;
            Console.ReadKey();
            await Recursion();
        }
    }

    static async Task Recursion() { await Main(); }
}

public partial class HoneyPotResult
{
    public HoneyPotResult(
            string result,
            string iban,
            string check1,
            string check2,
            string check3,
            string check4
    )
    {
        resultClass = new ResultClass(result, iban, check1, check2, check3, check4);
    }
    public ResultClass resultClass;
}

public partial class ResultClass
{
    public ResultClass(
        string result,
        string iban,
        string check1,
        string check2,
        string check3,
        string check4
        )
    {
        RESULT = result;
        IBAN = iban;
        CHECK1 = check1;
        CHECK2 = check2;
        CHECK3 = check3;
        CHECK4 = check4;
    }

    [JsonProperty("RESULT")]
    public static string RESULT { get; set; }

    [JsonProperty("IBAN")]
    public static string IBAN { get; set; }

    [JsonProperty("CHECK1")]
    public static string CHECK1 { get; set; }

    [JsonProperty("CHECK2")]
    public static string CHECK2 { get; set; }

    [JsonProperty("CHECK3")]
    public static string CHECK3 { get; set; }

    [JsonProperty("CHECK4")]
    public static string CHECK4 { get; set; }
}

internal static class Converter
{
    public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters = {
                new IsoDateTimeConverter {
                    DateTimeStyles = DateTimeStyles.AssumeUniversal
                }
            },
    };
}
