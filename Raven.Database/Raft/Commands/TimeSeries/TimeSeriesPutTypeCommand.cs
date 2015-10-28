using Rachis.Commands;

namespace Raven.Database.Raft.Commands.TimeSeries
{
	public class TimeSeriesPutTypeCommand : Command
	{
		public string Type { get; set; }
		public string[] Fields { get; set; }

		public static TimeSeriesPutTypeCommand Create(string type, string[] fields)
		{
			return new TimeSeriesPutTypeCommand
			{
				Type = type,
				Fields = fields,
			};
		}
	}
}