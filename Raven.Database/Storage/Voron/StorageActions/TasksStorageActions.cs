﻿// -----------------------------------------------------------------------
//  <copyright file="TasksStorageActions.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Diagnostics;
using System.Threading.Tasks;
using Lucene.Net.Search;

namespace Raven.Database.Storage.Voron.StorageActions
{
	using System;

	using Raven.Abstractions.Data;
	using Raven.Abstractions.Logging;
	using Raven.Database.Impl;
	using Raven.Database.Storage.Voron.Impl;
	using Raven.Database.Tasks;
	using Raven.Json.Linq;

	using global::Voron;
	using global::Voron.Impl;

	public class TasksStorageActions : StorageActionsBase, ITasksStorageActions
	{
		private static readonly ILog Logger = LogManager.GetCurrentClassLogger();

		private readonly TableStorage tableStorage;

		private readonly IUuidGenerator generator;

		private readonly WriteBatch writeBatch;

		public TasksStorageActions(TableStorage tableStorage, IUuidGenerator generator, SnapshotReader snapshot, WriteBatch writeBatch)
			: base(snapshot)
		{
			this.tableStorage = tableStorage;
			this.generator = generator;
			this.writeBatch = writeBatch;
		}

		public void AddTask(DatabaseTask task, DateTime addedAt)
		{
			var tasksByType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByType);
			var tasksByIndex = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByIndex);
			var tasksByIndexAndType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByIndexAndType);

			var type = task.GetType().FullName;
			var index = task.Index;
			var id = generator.CreateSequentialUuid(UuidType.Tasks);
			var idAsString = id.ToString();

			tableStorage.Tasks.Add(
				writeBatch,
				idAsString,
				new RavenJObject
				{
					{ "index", index },
					{ "id", id.ToByteArray() },
					{ "time", addedAt },
					{ "type", type },
					{ "task", task.AsBytes() }
				}, 0);

			tasksByType.MultiAdd(writeBatch, CreateKey(type), idAsString);
			tasksByIndex.MultiAdd(writeBatch, CreateKey(index), idAsString);
			tasksByIndexAndType.MultiAdd(writeBatch, CreateKey(index, type), idAsString);
		}

		public bool HasTasks
		{
			get { return ApproximateTaskCount > 0; }
		}

		public long ApproximateTaskCount
		{
			get
			{
				return tableStorage.GetEntriesCount(tableStorage.Tasks);
			}
		}

		public T GetMergedTask<T>() where T : DatabaseTask
		{
			var type = CreateKey(typeof(T).FullName);
			var tasksByType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByType);

			using (var iterator = tasksByType.MultiRead(Snapshot, type))
			{
				if (!iterator.Seek(Slice.BeforeAllKeys))
					return null;

				do
				{
					ushort version;
					var value = LoadJson(tableStorage.Tasks, iterator.CurrentKey, writeBatch, out version);

					DatabaseTask task;
					try
					{
						task = DatabaseTask.ToTask(value.Value<string>("type"), value.Value<byte[]>("task"));
					}
					catch (Exception e)
					{
						Logger.ErrorException(
							string.Format("Could not create instance of a task: {0}", value),
							e);
						continue;
					}

					MergeSimilarTasks(task, value.Value<byte[]>("id"));
					RemoveTask(iterator.CurrentKey, task.Index, type);

					return (T)task;
				}
				while (iterator.MoveNext());
			}

			return null;
		}

		private void MergeSimilarTasks(DatabaseTask task, byte[] taskId)
		{
			var id = Etag.Parse(taskId);
			var type = task.GetType().FullName;
			var tasksByIndexAndType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByIndexAndType);

			using (var iterator = tasksByIndexAndType.MultiRead(Snapshot, CreateKey(task.Index, type)))
			{
				if (!iterator.Seek(Slice.BeforeAllKeys))
					return;

				int totalTaskCount = 0;

				do
				{
					var currentId = Etag.Parse(iterator.CurrentKey.ToString());
					if (currentId == id)
						continue;

					ushort version;
					var value = LoadJson(tableStorage.Tasks, iterator.CurrentKey, writeBatch, out version);

					DatabaseTask existingTask;
					try
					{
						existingTask = DatabaseTask.ToTask(value.Value<string>("type"), value.Value<byte[]>("task"));
					}
					catch (Exception e)
					{
						Logger.ErrorException(
							string.Format("Could not create instance of a task: {0}", value),
							e);

						RemoveTask(iterator.CurrentKey, task.Index, type);
						continue;
					}

					task.Merge(existingTask);
					RemoveTask(iterator.CurrentKey, task.Index, type);

					if (totalTaskCount++ > 1024)
						break;
				}
				while (iterator.MoveNext());
			}
		}

		private void RemoveTask(Slice taskId, int index, string type)
		{
			var tasksByType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByType);
			var tasksByIndex = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByIndex);
			var tasksByIndexAndType = tableStorage.Tasks.GetIndex(Tables.Tasks.Indices.ByIndexAndType);

			tableStorage.Tasks.Delete(writeBatch, taskId);
			tasksByType.MultiDelete(writeBatch, CreateKey(type), taskId);
			tasksByIndex.MultiDelete(writeBatch, CreateKey(index), taskId);
			tasksByIndexAndType.MultiDelete(writeBatch, CreateKey(index, type), taskId);
		}


		public System.Collections.Generic.IEnumerable<TaskMetadata> GetPendingTasksForDebug()
		{
			if(!HasTasks)
				yield break;

			using (var taskIterator = tableStorage.Tasks.Iterate(Snapshot, writeBatch))
			{
				if(!taskIterator.Seek(Slice.BeforeAllKeys))
					yield break;

				do
				{
					ushort version;
					var taskData = LoadJson(tableStorage.Tasks, taskIterator.CurrentKey, writeBatch, out version); 
					if (taskData == null) 
							throw new InvalidOperationException("Retrieved a pending task object, but was unable to parse it. This is probably a data corruption or a bug.");

						TaskMetadata pendingTasksForDebug;
						try
						{
							pendingTasksForDebug = new TaskMetadata
							{
								Id = Etag.Parse(taskData.Value<byte[]>("id")),
								AddedTime = DateTime.Parse(taskData.Value<string>("time")),
								Type = taskData.Value<string>("type"),
								IndexId = taskData.Value<int>("index")
							};
						}
						catch (Exception e)
						{
							throw new InvalidOperationException("The pending task record was parsed, but contained invalid values. See more details at inner exception.",e);
						}

						yield return pendingTasksForDebug;
				} while (taskIterator.MoveNext());
			}
			

		}
	}
}