﻿using System;
using System.Collections.Async;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MimeKit;
using Walterlv.AssembleMailing.Models;
using Walterlv.AssembleMailing.Utils;

namespace Walterlv.AssembleMailing.Mailing
{
    public class MailBoxCache
    {
        private static readonly ConcurrentDictionary<string, MailBoxCache> AllCache
            = new ConcurrentDictionary<string, MailBoxCache>();

        public static MailBoxCache Get(string directory, MailBoxConnectionInfo info, IPasswordManager passwordManager)
        {
            if (!AllCache.TryGetValue(info.Address, out var cache))
            {
                cache = new MailBoxCache(Path.Combine(directory, info.Address), info, passwordManager);
                AllCache[info.Address] = cache;
            }

            return cache;
        }

        public MailBoxCache(string directory, MailBoxConnectionInfo info, IPasswordManager passwordManager)
        {
            Directory = directory;
            ConnectionInfo = info;
            _passwordManager = passwordManager;
        }

        public string Directory { get; }

        public MailBoxConnectionInfo ConnectionInfo { get; }

        public async Task<IList<MailBoxFolder>> LoadMailFoldersAsync()
        {
            var folderCache = new FileSerializor<List<MailBoxFolder>>(Path.Combine(Directory, "folders.json"));
            var cachedFolder = await folderCache.ReadAsync();
            if (cachedFolder.Any())
            {
                return cachedFolder;
            }

            FillPassword(ConnectionInfo);
            var result = new List<MailBoxFolder>();
            using (var client = await new IncomingMailClient(ConnectionInfo).ConnectAsync())
            {
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadOnly);

                var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0]);
                foreach (var folder in new[] {inbox}.Union(folders))
                {
                    result.Add(new MailBoxFolder
                    {
                        Name = folder.Name,
                        FullName = folder.FullName,
                    });
                }
            }

            await folderCache.SaveAsync(result);

            return result;
        }

        public async Task<IList<MailSummary>> LoadMailsAsync(MailBoxFolder folder, int start = 0, int length = 20)
        {
            var cache = new FileSerializor<List<MailSummary>>(
                Path.Combine(Directory, "Folders", folder.FullName, "summaries.json"));
            if (start == 0)
            {
                // Temporarily load cache only for first 20.
                var cachedSummary = await cache.ReadAsync();
                if (cachedSummary.Any())
                {
                    return cachedSummary;
                }
            }

            FillPassword(ConnectionInfo);
            var result = new List<MailSummary>();
            using (var client = await new IncomingMailClient(ConnectionInfo).ConnectAsync())
            {
                var mailFolder = await client.GetFolderAsync(folder.FullName);
                mailFolder.Open(FolderAccess.ReadOnly);

                var fetchingCount = mailFolder.Count < start + length ? mailFolder.Count : start + length;
                if (fetchingCount > 0)
                {
                    var messageSummaries = await mailFolder.FetchAsync(mailFolder.Count - fetchingCount,
                        mailFolder.Count - 1 - start,
                        MessageSummaryItems.UniqueId | MessageSummaryItems.Full);
                    foreach (var summary in messageSummaries.Reverse())
                    {
                        TextPart body;
                        try
                        {
                            body = (TextPart) await mailFolder.GetBodyPartAsync(summary.UniqueId, summary.TextBody);
                        }
                        catch (Exception ex)
                        {
                            // Temporarily catch all exceptions, and it will be handled correctly after main project is about to finish.
                            body = null;
                        }

                        var mailGroup = new MailSummary
                        {
                            Title = summary.Envelope.From.Select(x => x.Name).FirstOrDefault() ?? "(Anonymous)",
                            Topic = summary.Envelope.Subject,
                            Excerpt = body?.Text?.Replace(Environment.NewLine, " "),
                            MailIds = new List<uint> {summary.UniqueId.Id},
                        };
                        result.Add(mailGroup);
                    }
                }
            }

            cache.Save(result);

            return result;
        }

        public async Task<MailContentCache> LoadMailAsync(MailBoxFolder folder, uint id)
        {
            var htmlFileName = Path.Combine(Directory, "Mails", $"{id}.html");
            var contentfileName = Path.Combine(Directory, "Mails", $"{id}.json");
            var cache = new FileSerializor<MailContentCache>(contentfileName);

            var contentCache = await cache.ReadAsync();
            if (contentCache.Content != null && File.Exists(contentCache.HtmlFileName))
            {
                return contentCache;
            }


            FillPassword(ConnectionInfo);
            using (var client = await new IncomingMailClient(ConnectionInfo).ConnectAsync())
            {
                var mailFolder = await client.GetFolderAsync(folder.FullName);
                mailFolder.Open(FolderAccess.ReadOnly);

                var message = await mailFolder.GetMessageAsync(new UniqueId(id));
                var htmlBody = message.HtmlBody;

                var content = new MailContentCache
                {
                    Content = message.TextBody ?? htmlBody,
                    HtmlFileName = htmlFileName,
                };
                await cache.SaveAsync(content);
                File.WriteAllText(htmlFileName, htmlBody);

                return content;
            }
        }

        public IAsyncEnumerable<MailContentCache> EnumerateMailsAsync(MailBoxFolder folder)
        {
            return new AsyncEnumerable<MailContentCache>(async yield =>
            {
                var startIndex = 0;
                while (true)
                {
                    var summaries = await LoadMailsAsync(folder, startIndex, startIndex + 20);
                    var finished = true;
                    foreach (var summary in summaries)
                    {
                        finished = false;
                        var contentCache = await LoadMailAsync(folder, summary.MailIds.First());
                        await yield.ReturnAsync(contentCache);
                    }

                    startIndex += 20;

                    if (finished)
                    {
                        break;
                    }
                }
                yield.Break();
            });
        }

        private void FillPassword(MailBoxConnectionInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info.Address))
            {
                info.Password = _passwordManager.Retrieve(info.Address);
            }
        }

        private readonly IPasswordManager _passwordManager;
    }
}
