namespace FileExplorer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Skyline.DataMiner.Analytics.GenericInterface;
    using Skyline.DataMiner.Utils.SecureCoding.SecureIO;

	/// <summary>
	/// Represents a data source.
	/// See: https://aka.dataminer.services/gqi-external-data-source for a complete example.
	/// </summary>
    [GQIMetaData(Name = "FileExplorer SCO")]
    public sealed class FileExplorer : IGQIDataSource
        , IGQIInputArguments
        , IGQIUpdateable
    {
        private readonly GQIStringArgument _pathArg = new GQIStringArgument("Path")
        {
            IsRequired = false,
            DefaultValue = "C:\\Skyline DataMiner\\Documents\\DMA_COMMON_DOCUMENTS",
        };

        private readonly GQIStringArgument _patternArg = new GQIStringArgument("Search Pattern")
        {
            IsRequired = false,
            DefaultValue = "*.*",
        };

        private readonly GQIBooleanArgument _recursiveArg = new GQIBooleanArgument("Recursive")
        {
            IsRequired = false,
            DefaultValue = false,
        };

        private string _path;
        private string _pattern;
        private bool _recursive;

        private FileSystemWatcher _watcher;
        private IGQIUpdater _updater;

        public GQIArgument[] GetInputArguments()
        {
            return new GQIArgument[]
            {
                _pathArg,
                _patternArg,
                _recursiveArg,
            };
        }

        public OnArgumentsProcessedOutputArgs OnArgumentsProcessed(OnArgumentsProcessedInputArgs args)
        {
            _path = args.GetArgumentValue(_pathArg);
            _pattern = args.GetArgumentValue(_patternArg);
            _recursive = args.GetArgumentValue(_recursiveArg);
            return default;
        }

        public GQIColumn[] GetColumns()
        {
            return new GQIColumn[]
            {
                new GQIStringColumn("File Name"),
                new GQIStringColumn("Path"),
                new GQIDateTimeColumn("Created"),
                new GQIDateTimeColumn("Last Modified"),
                new GQIDoubleColumn("Size"),
                new GQIStringColumn("Type"),
                new GQIBooleanColumn("Read Only"),
            };
        }

        public void OnStartUpdates(IGQIUpdater updater)
        {
            _updater = updater;

            _watcher = new FileSystemWatcher(_path, _pattern)
            {
                IncludeSubdirectories = _recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
        }

        public GQIPage GetNextPage(GetNextPageInputArgs args)
        {
            var rows = new List<GQIRow>();
            var securePath = _recursive ? SecurePath.ConstructSecurePathWithSubDirectories(_path,string.Empty) : SecurePath.ConstructSecurePath(_path);

            try
            {
                var option = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (var filePath in Directory.GetFiles(securePath, _pattern, option))
                {
                    var info = new FileInfo(filePath);
                    rows.Add(CreateRow(info));
                }
            }
            catch (Exception ex)
            {
				throw new Exception($"Failed to retrieve files: {ex.Message}");
            }

            return new GQIPage(rows.ToArray())
            {
                HasNextPage = false,
            };
        }

        public void OnStopUpdates()
        {
            DisposeWatcher();
            _updater = null;
        }

        private GQIRow CreateRow(FileInfo info)
        {
            return new GQIRow(
                key: info.FullName,
                cells: new GQICell[]
                {
                        new GQICell { Value = info.Name },
                        new GQICell { Value = info.FullName },
                        new GQICell { Value = info.CreationTimeUtc },
                        new GQICell { Value = info.LastWriteTimeUtc },
                        new GQICell { Value = (double)info.Length },
                        new GQICell { Value = info.Extension.TrimStart('.').ToUpper() },
                        new GQICell { Value = info.IsReadOnly },
                });
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            var info = new FileInfo(e.FullPath);

            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                    if (info.Exists)
                    _updater.AddRow(CreateRow(info));
                    break;

                case WatcherChangeTypes.Deleted:
                    _updater.RemoveRow(e.FullPath);
                    break;

                case WatcherChangeTypes.Changed:
                    if (info.Exists)
                    _updater.UpdateRow(CreateRow(info));
                    break;
                case WatcherChangeTypes.Renamed:
                    var renamedArgs = e as RenamedEventArgs;
                    if(renamedArgs != null)
					{
						_updater.RemoveRow(renamedArgs.OldFullPath);
						if (info.Exists)
							_updater.AddRow(CreateRow(info));
					}

                    break;
            }
        }

        private void DisposeWatcher()
        {
            if (_watcher == null)
                return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Deleted -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileSystemChanged;
            _watcher.Changed -= OnFileSystemChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}