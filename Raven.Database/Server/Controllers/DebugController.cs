﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;
using System.Web.Routing;
using Raven.Abstractions.Data;
using Raven.Database.Extensions;
using Raven.Database.Linq;
using Raven.Database.Linq.Ast;
using Raven.Database.Server.Abstractions;
using Raven.Database.Server.RavenFS.Extensions;
using Raven.Database.Storage;
using Raven.Database.Tasks;
using Raven.Json.Linq;

namespace Raven.Database.Server.Controllers
{
	[RoutePrefix("")]
	public class DebugController : RavenDbApiController
	{
		[HttpGet]
		[Route("debug/changes")]
		[Route("databases/{databaseName}/debug/changes")]
		public HttpResponseMessage Changes()
		{
			return GetMessageWithObject(Database.TransportState.DebugStatuses);
		}

		[HttpGet]
		[Route("debug/config")]
		[Route("databases/{databaseName}/debug/config")]
		public HttpResponseMessage Config()
		{
			var cfg = RavenJObject.FromObject(Database.Configuration);
			cfg["OAuthTokenKey"] = "<not shown>";
			var changesAllowed = Database.Configuration.Settings["Raven/Versioning/ChangesToRevisionsAllowed"];

			if (string.IsNullOrWhiteSpace(changesAllowed) == false)
				cfg["Raven/Versioning/ChangesToRevisionsAllowed"] = changesAllowed;

			return GetMessageWithObject(cfg);
		}

		[HttpGet]
		[Route("debug/docrefs")]
		[Route("databases/{databaseName}/debug/docrefs")]
		public HttpResponseMessage Docrefs(string id)
		{
			var totalCount = -1;
			List<string> results = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				totalCount = accessor.Indexing.GetCountOfDocumentsReferencing(id);
				results =
					accessor.Indexing.GetDocumentsReferencing(id)
							.Skip(GetStart())
							.Take(GetPageSize(Database.Configuration.MaxPageSize))
							.ToList();
			});

			return GetMessageWithObject(new
			{
				TotalCount = totalCount,
				Results = results
			});
		}

		[HttpGet]
		[Route("debug/index-fields")]
		[Route("databases/{databaseName}/debug/index-fields")]
		public async Task<HttpResponseMessage> IndexFields()
		{
			var indexStr = await ReadStringAsync();
			var mapDefinition = indexStr.Trim().StartsWith("from")
				? QueryParsingUtils.GetVariableDeclarationForLinqQuery(indexStr, true)
				: QueryParsingUtils.GetVariableDeclarationForLinqMethods(indexStr, true);

			var captureSelectNewFieldNamesVisitor = new CaptureSelectNewFieldNamesVisitor();
			mapDefinition.AcceptVisitor(captureSelectNewFieldNamesVisitor, null);

			return GetMessageWithObject(new { captureSelectNewFieldNamesVisitor.FieldNames });
		}

		[HttpGet]
		[Route("debug/list")]
		[Route("databases/{databaseName}/debug/list")]
		public HttpResponseMessage List(string id)
		{
			var listName = id;
			var key = InnerRequest.RequestUri.ParseQueryString()["key"];
			if (key == null)
				throw new ArgumentException("Key query string variable is mandatory");

			ListItem listItem = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				listItem = accessor.Lists.Read(listName, key);
				if (listItem == null)
					throw new HttpException(400, "Not found");
			});

			if (listItem == null)
				throw new HttpException(400, "Not found");

			return GetMessageWithObject(listItem);
		}

		[HttpGet]
		[Route("debug/queries")]
		[Route("databases/{databaseName}/debug/queries")]
		public HttpResponseMessage Queries()
		{
			return GetMessageWithObject(Database.WorkContext.CurrentlyRunningQueries);
		}

		[HttpGet]
		[Route("debug/sl0w-d0c-c0unts")]
		[Route("databases/{databaseName}/debug/sl0w-d0c-c0unts")]
		public HttpResponseMessage SlowDocCounts()
		{
			DebugDocumentStats stat = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				stat = accessor.Documents.GetDocumentStatsVerySlowly();
			});

			return GetMessageWithObject(stat);
		}

		[HttpGet]
		[HttpPost]
		[Route("debug/user-info")]
		[Route("databases/{databaseName}/debug/user-info")]
		public HttpResponseMessage UserInfo()
		{
			var principal = User;
			if (principal == null || principal.Identity == null || principal.Identity.IsAuthenticated == false)
			{
				var anonymous = new UserInfo
				{
					Remark = "Using anonymous user",
					IsAdminGlobal = DatabasesLandlord.SystemConfiguration.AnonymousUserAccessMode == AnonymousUserAccessMode.Admin
				};
				return GetMessageWithObject(anonymous);
			}

			var windowsPrincipal = principal as WindowsPrincipal;
			if (windowsPrincipal != null)
			{
				var windowsUser = new UserInfo
				{
					Remark = "Using windows auth",
					User = windowsPrincipal.Identity.Name,
					IsAdminGlobal =
						windowsPrincipal.IsAdministrator(DatabasesLandlord.SystemConfiguration.AnonymousUserAccessMode)
				};

				return GetMessageWithObject(windowsUser);
			}

			var principalWithDatabaseAccess = principal as PrincipalWithDatabaseAccess;
			if (principalWithDatabaseAccess != null)
			{
				var windowsUserWithDatabase = new UserInfo
				{
					Remark = "Using windows auth",
					User = principalWithDatabaseAccess.Identity.Name,
					IsAdminGlobal =
						principalWithDatabaseAccess.IsAdministrator(
							DatabasesLandlord.SystemConfiguration.AnonymousUserAccessMode),
					IsAdminCurrentDb = principalWithDatabaseAccess.IsAdministrator(Database),
					Databases =
						principalWithDatabaseAccess.AdminDatabases.Concat(
							principalWithDatabaseAccess.ReadOnlyDatabases)
												   .Concat(principalWithDatabaseAccess.ReadWriteDatabases)
												   .Select(db => new DatabaseInfo
												   {
													   Database = db,
													   IsAdmin = principal.IsAdministrator(db)
												   }).ToList(),
					AdminDatabases = principalWithDatabaseAccess.AdminDatabases,
					ReadOnlyDatabases = principalWithDatabaseAccess.ReadOnlyDatabases,
					ReadWriteDatabases = principalWithDatabaseAccess.ReadWriteDatabases
				};

				return GetMessageWithObject(windowsUserWithDatabase);
			}

			var oAuthPrincipal = principal as OAuthPrincipal;
			if (oAuthPrincipal != null)
			{
				var oAuth = new UserInfo
				{
					Remark = "Using OAuth",
					User = oAuthPrincipal.Name,
					IsAdminGlobal = oAuthPrincipal.IsAdministrator(DatabasesLandlord.SystemConfiguration.AnonymousUserAccessMode),
					IsAdminCurrentDb = oAuthPrincipal.IsAdministrator(Database),
					Databases = oAuthPrincipal.TokenBody.AuthorizedDatabases
											  .Select(db => new DatabaseInfo
											  {
												  Database = db.TenantId,
												  IsAdmin = principal.IsAdministrator(db.TenantId)
											  }).ToList(),
					AccessTokenBody = oAuthPrincipal.TokenBody,
				};

				return GetMessageWithObject(oAuth);
			}

			var unknown = new UserInfo
			{
				Remark = "Unknown auth",
				Principal = principal
			};

			return GetMessageWithObject(unknown);
		}

		[HttpGet]
		[Route("debug/tasks")]
		[Route("databases/{databaseName}/debug/tasks")]
		public HttpResponseMessage Tasks()
		{
			IList<TaskMetadata> tasks = null;
			Database.TransactionalStorage.Batch(accessor =>
			{
				tasks = accessor.Tasks
					.GetPendingTasksForDebug()
					.ToList();
			});

			foreach (var taskMetadata in tasks)
			{
				var indexInstance = Database.IndexStorage.GetIndexInstance(taskMetadata.IndexId);
				if (indexInstance != null)
					taskMetadata.IndexName = indexInstance.PublicName;
			}

			return GetMessageWithObject(tasks);
		}

		[HttpGet]
		[Route("debug/routes")]
		[Description(@"Output the debug information for all the supported routes in Raven Server.")]
		public HttpResponseMessage Routes()
		{
			var routes = new SortedDictionary<string, RouteInfo>();

			foreach (var route in ControllerContext.Configuration.Routes)
			{
				var inner = route as IEnumerable<IHttpRoute>;
				if (inner == null) continue;

				foreach (var httpRoute in inner)
				{
					var key = httpRoute.RouteTemplate;
					bool forDatabase = false;
					if (key.StartsWith("databases/{databaseName}/"))
					{
						key = key.Substring("databases/{databaseName}/".Length);
						forDatabase = true;
					}
					var data = new RouteInfo(key);
					if (routes.ContainsKey(key))
						data = routes[key];

					if (forDatabase)
						data.CanRunForSpecificDatabase = true;

					var actions = (IEnumerable<ReflectedHttpActionDescriptor>)httpRoute.DataTokens["actions"];

					foreach (var reflectedHttpActionDescriptor in actions)
					{
						
						foreach (var httpMethod in reflectedHttpActionDescriptor.SupportedHttpMethods)
						{
							if (data.Methods.Any(method => method.Name == httpMethod.Method)) 
								continue;

							string description = null;
							var descriptionAttibute =
								reflectedHttpActionDescriptor.MethodInfo.CustomAttributes.FirstOrDefault(attributeData => attributeData.AttributeType == typeof(DescriptionAttribute));
							if(descriptionAttibute != null)
								description = descriptionAttibute.ConstructorArguments[0].Value.ToString();
								
							data.Methods.Add(new Method
							{
								Name = httpMethod.Method,
								Description = description
							});
						}
					}

					routes[key] = data;
				}
			}

			return GetMessageWithObject(routes);
		}
	}

	public class RouteInfo
	{
		public string Key { get; set; }
		public List<Method> Methods { get; set; }

		public bool CanRunForSpecificDatabase { get; set; }

		public RouteInfo(string key)
		{
			Key = key;
			Methods = new List<Method>();
		}
	}

	public class Method
	{
		public string Name { get; set; }
		public string Description { get; set; }
	}
}