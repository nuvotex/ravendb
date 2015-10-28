// -----------------------------------------------------------------------
//  <copyright file="AppendPointCommand.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Threading.Tasks;
using Rachis.Commands;

namespace Raven.Database.TimeSeries.Raft.Commands
{
	public class AppendPointCommand : Command
	{
		public string Type { get; set; } 
		public string Key { get; set; } 
		public DateTimeOffset At { get; set; } 
		public double[] Values { get; set; }
		public string TimeSeries { get; set; }

		public AppendPointCommand()
		{
			Completion = new TaskCompletionSource<object>();
		}
	}
}