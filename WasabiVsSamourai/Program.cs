using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using WasabiVsSamourai.Helpers;

namespace WasabiVsSamourai
{
	internal class Program
	{
		// TODO:
		// :) Establish RPC connecton with Bitcoin Core.
		// :) Go through all the blocks and txs from June 1. (Whirlpool launched the end of June?)
		// :) Identify Wasabi coinjoins (2 coord addresses + indistinguishable outputs > 2.)
		// :) Identify Samourai coinjoins (5 in, 5 out + equal output amounts.)
		// :) Count the total number of txs.
		// :) Count the total volume.
		// :) Count the mixed volume.
		// :) Count the mixed volume weighted with the anonset gained.
		// :) Write out monthly comparision.
		// :) Publish to GitHub, were the readme is the output.
		private static async Task Main(string[] args)
		{
			Console.WriteLine("Hello World! This software compares Wasabi and Samourai coinjoins. Although, I'm not sure it makes much sense, because Wasabi is trustless, and Samourai is untrusted.");
			Console.WriteLine();

			Logger.InitializeDefaults();

			ParseArgs(args, out NetworkCredential rpcCred);

			var rpcConf = new RPCCredentialString
			{
				UserPassword = rpcCred
			};
			var client = new RPCClient(rpcConf, Network.Main);

			using (BenchmarkLogger.Measure(operationName: "CompareCoinjoinsAsync"))
			{
				await CompareCoinjoinsAsync(client);
			}

			Console.WriteLine();
			Console.WriteLine("Press a button to exit...");
			Console.ReadKey();
		}

		private static async Task CompareCoinjoinsAsync(RPCClient client)
		{
			var bestHeight = await client.GetBlockCountAsync();

			int percentageDone = 0;
			// Starts with June 1.
			var height = 578718;
			var totalBlocks = bestHeight - height;
			Console.WriteLine($"{totalBlocks} blocks will be analyzed.");

			var months = new Dictionary<YearMonth, MonthStats>();
			while (true)
			{
				var block = await client.GetBlockAsync(height);

				var timeStamp = block.Header.BlockTime;
				var monthStamp = new YearMonth { Year = timeStamp.Year, Month = timeStamp.Month };
				MonthStats stat;
				if (months.TryGetValue(monthStamp, out MonthStats s))
				{
					stat = s;
				}
				else
				{
					stat = new MonthStats();
					months.Add(monthStamp, stat);

					var prevMonth = monthStamp.Month - 1;
					YearMonth prevYearMonth = new YearMonth { Year = monthStamp.Year, Month = prevMonth };
					if (prevMonth >= 1 && months.TryGetValue(prevYearMonth, out MonthStats prevS))
					{
						Console.WriteLine(prevYearMonth);
						Display(prevS);
					}
				}

				foreach (var tx in block.Transactions)
				{
					var isWasabiCj = tx.Outputs.Any(x => WasabiCoordScripts.Contains(x.ScriptPubKey)) && tx.GetIndistinguishableOutputs(includeSingle: false).Any(x => x.count > 2);
					if (isWasabiCj)
					{
						stat.WasabiCjs.Add(tx);
					}

					var isSamouraiCj = tx.Inputs.Count == 5 && tx.Outputs.Count == 5 && tx.Outputs.Select(x => x.Value).Distinct().Count() == 1 && SamouraiPools.Any(x => x.Almost(tx.Outputs.First().Value, MaxSamouraiPoolFee));
					if (isSamouraiCj)
					{
						stat.SamouraiCjs.Add(tx);
					}
				}

				int blocksLeft = bestHeight - height;
				var tempPercentageDone = percentageDone;
				percentageDone = (totalBlocks - blocksLeft) / (totalBlocks / 100);
				if (percentageDone != tempPercentageDone)
				{
					Console.WriteLine($"Progress: {percentageDone}%");
				}
				if (blocksLeft >= 0)
				{
					// Refresh bestHeight and if still no new block, then end here.
					bestHeight = await client.GetBlockCountAsync();
					if (bestHeight <= height)
					{
						break;
					}
				}
				height++;
			}

			foreach (var month in months)
			{
				Console.WriteLine();
				Console.WriteLine(month.Key);
				Display(month.Value);
			}
		}

		private static void Display(MonthStats prevS)
		{
			Console.WriteLine($"Wasabi transaction count:                   {prevS.WasabiCjs.Count}");
			Console.WriteLine($"Samourai transaction count:                 {prevS.SamouraiCjs.Count}");
			Console.WriteLine($"Wasabi total volume:                        {prevS.WasabiTotalVolume.GetWholeBTC()} BTC");
			Console.WriteLine($"Samourai total volume:                      {prevS.SamouraiTotalVolume.GetWholeBTC()} BTC");
			Console.WriteLine($"Wasabi total mixed volume:                  {prevS.WasabiTotalMixedVolume.GetWholeBTC()} BTC");
			Console.WriteLine($"Samourai total mixed volume:                {prevS.SamouraiTotalMixedVolume.GetWholeBTC()} BTC");
			Console.WriteLine($"Wasabi anonset weighted volume mix score:   {prevS.WasabiTotalAnonsetWeightedMixedVolume.GetWholeBTC()}");
			Console.WriteLine($"Samourai anonset weighted volume mix score: {prevS.SamouraiTotalAnonsetWeightedMixedVolume.GetWholeBTC()}");
		}

		public static IEnumerable<Script> WasabiCoordScripts = new Script[]
		{
			BitcoinAddress.Create("bc1qs604c7jv6amk4cxqlnvuxv26hv3e48cds4m0ew", Network.Main).ScriptPubKey,
			BitcoinAddress.Create("bc1qa24tsgchvuxsaccp8vrnkfd85hrcpafg20kmjw", Network.Main).ScriptPubKey
		};

		public static IEnumerable<Money> SamouraiPools = new Money[]
		{
			Money.Coins(0.01m),
			Money.Coins(0.05m)
		};

		public static Money MaxSamouraiPoolFee = Money.Coins(0.0011m);

		private static void ParseArgs(string[] args, out NetworkCredential cred)
		{
			string rpcUser = null;
			string rpcPassword = null;

			var rpcUserArg = "--rpcuser=";
			var rpcPasswordArg = "--rpcpassword=";
			foreach (var arg in args)
			{
				var idx = arg.IndexOf(rpcUserArg, StringComparison.Ordinal);
				if (idx == 0)
				{
					rpcUser = arg.Substring(idx + rpcUserArg.Length);
				}

				idx = arg.IndexOf(rpcPasswordArg, StringComparison.Ordinal);
				if (idx == 0)
				{
					rpcPassword = arg.Substring(idx + rpcPasswordArg.Length);
				}
			}

			cred = new NetworkCredential(rpcUser, rpcPassword);
		}
	}
}
