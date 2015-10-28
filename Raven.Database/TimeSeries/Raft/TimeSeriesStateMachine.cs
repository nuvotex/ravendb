// -----------------------------------------------------------------------
//  <copyright file="ClusterStateMachine.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Rachis;
using Rachis.Commands;
using Rachis.Interfaces;
using Rachis.Messages;
using Raven.Abstractions.Data;
using Raven.Abstractions.Logging;
using Raven.Database.Impl;
using Raven.Database.Raft.Commands;
using Raven.Database.Raft.Storage.Handlers;
using Raven.Database.Server.Tenancy;
using Raven.Database.Storage;
using Raven.Database.Util;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;

namespace Raven.Database.TimeSeries.Raft
{
	public class TimeSeriesStateMachine : IRaftStateMachine
	{
		public void Dispose()
		{
			throw new NotImplementedException();
		}

		public long LastAppliedIndex { get; }
		public void Apply(LogEntry entry, Command cmd)
		{
			throw new NotImplementedException();
		}

		public bool SupportSnapshots { get; }
		public void CreateSnapshot(long index, long term, ManualResetEventSlim allowFurtherModifications)
		{
			throw new NotImplementedException();
		}

		public ISnapshotWriter GetSnapshotWriter()
		{
			throw new NotImplementedException();
		}

		public void ApplySnapshot(long term, long index, Stream stream)
		{
			throw new NotImplementedException();
		}
	}
}