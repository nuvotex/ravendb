// -----------------------------------------------------------------------
//  <copyright file="BaseTimeSeriesApiController.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using Rachis;
using Rachis.Utils;
using Raven.Abstractions.Extensions;
using Raven.Database.Common;
using Raven.Database.Raft;
using Raven.Database.Server.Tenancy;
using Raven.Database.TimeSeries.Raft;

namespace Raven.Database.TimeSeries.Controllers
{
	public abstract class BaseTimeSeriesApiController : ResourceApiController<TimeSeriesStorage, TimeSeriesLandlord>
	{
		public TimeSeriesStateMachine StateMachine { get; private set; }

		public RaftEngine RaftEngine { get; private set; }

		public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
		{
			RaftEngine = (RaftEngine)controllerContext.Configuration.Properties[typeof(TimeSeriesLandlord)];
			StateMachine = (TimeSeriesStateMachine)RaftEngine.StateMachine;

			try
			{
				return await base.ExecuteAsync(controllerContext, cancellationToken).ConfigureAwait(false);
			}
			catch (NotLeadingException)
			{
				var currentLeader = RaftEngine.CurrentLeader;
				if (currentLeader == null)
				{
					return GetMessageWithString("No current leader, try again later", HttpStatusCode.PreconditionFailed);
				}
				var leaderNode = RaftEngine.CurrentTopology.GetNodeByName(currentLeader);
				if (leaderNode == null)
				{
					return GetMessageWithString("Current leader " + currentLeader + " is not found in the topology. This should not happen.", HttpStatusCode.PreconditionFailed);
				}
				return new HttpResponseMessage(HttpStatusCode.Redirect)
				{
					Headers =
					{
						Location = leaderNode.Uri
					}
				};
			}
		}

		public ClusterManager ClusterManager
		{
			get { return ((Reference<ClusterManager>) Configuration.Properties[typeof (ClusterManager)]).Value; }
		}

		protected string TimeSeriesName
		{
			get { return ResourceName; }
		}

		protected TimeSeriesStorage TimeSeries
		{
			get { return Resource; }
		}

		public override ResourceType ResourceType
		{
			get { return ResourceType.TimeSeries; }
		}

		public override void MarkRequestDuration(long duration)
		{
			if (Resource == null)
				return;

			Resource.MetricsTimeSeries.RequestDurationMetric.Update(duration);
		}
	}
}