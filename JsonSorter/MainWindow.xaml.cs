using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Automata.IO;
using JsonSorter.Code.Extensions;

namespace JsonSorter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window , INotifyPropertyChanged
    {
        private Directory _sourceFolder;
        private ObservableCollection<JsonSortConfig> _configs;

        private List<JsonRelatedFile> Files;
        private int _progress;

        public Directory SourceFolder
        {
            get => _sourceFolder;
            set
            {
                if (Equals(value, _sourceFolder)) return;
                _sourceFolder = value;
                OnPropertyChanged();
            }
        }


        public ObservableCollection<JsonSortConfig> Configs
        {
            get => _configs;
            set
            {
                if (Equals(value, _configs)) return;
                _configs = value;
                OnPropertyChanged();
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                if (value == _progress) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            Configs = new ObservableCollection<JsonSortConfig>();

            InitializeComponent();

            ReadJsonSortConfigs();
        }

        public async void ReadJsonSortConfigs()
        {
            var root = new Directory(AppDomain.CurrentDomain.BaseDirectory).Directory("root");
            var configsFolder = root.Directory("configs");

            configsFolder.Create();

            await foreach (var file in configsFolder.EnumerateFiles())
            {
                if (file.Extension() == ".json")
                {
                    var jsonSortConfig = await JsonSerializer.DeserializeAsync<JsonSortConfig>(file.Stream(),
                        new JsonSerializerOptions(){PropertyNamingPolicy = null});
                    Configs.Add(jsonSortConfig);
                }
            }
        }

        private void SelectSourceFolderClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            if (dialog.ShowDialog() is true)
            {
                var path = dialog.SelectedPath;

                SourceFolder = new Directory(path);
            }
        }

        private async void SortClick(object sender, RoutedEventArgs e)
        {
            Progress = 0;
            var root = SourceFolder;

            Files = new List<JsonRelatedFile>();
            await foreach (var file in root.EnumerateFilesDeep())
            {
                try
                {
                    if (file.Extension() != ".json")
                        continue;

                    var content = await file.ReadAsync();
                    var node = JsonNode.Parse(content);

                    var config = SpecifyConfig(node);
                    if (config is null)
                        continue;

                    var jsonRelatedFile = new JsonRelatedFile(config, file, node);
                    Files.Add(jsonRelatedFile);
                    Log($"{jsonRelatedFile.File.Prefix()} added");
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }


            var index = 0D;
            foreach (var jsonRelatedFile in Files)
            {
                try
                {
                    await jsonRelatedFile.Sort();
                    index++;
                    Progress = (int)Math.Floor((index / (double)Files.Count)*100);
                    Log($"{jsonRelatedFile.File.Prefix()} sorted");
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }

            MessageBox.Show("All sorted!");
        }

        private JsonSortConfig? SpecifyConfig(JsonNode? json)
        {
            if (json is null)
                return null;

            foreach (var config in Configs)
            {
                if (json[config.UniqueField] is null)
                    continue;
                else
                    return config;
            }

            return null;
        }

        public async void Log(string log)
        {
            MessageLogBox.Text += log + "\n";
            MessageLogBox.GoToLine(MessageLogBox.Lines.Count-1);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}


public class JsonSortConfig
{
    [JsonPropertyName("object_type")]
    public string ObjectType { get; set; }

    [JsonPropertyName("unique_field")]
    public string UniqueField { get; set; }

    [JsonPropertyName("nodes_order")]
    public List<string> NodesOrder { get; set; }

    public JsonSortConfig()
    {

    }

    public JsonSortConfig(List<string> jsonNodesOrder, string objectType, string uniqueField)
    {
        NodesOrder = jsonNodesOrder;
        ObjectType = objectType;
        UniqueField = uniqueField;
    }
}

public class SortedJson
{
    public JsonSortConfig Config;

    public List<KeyValuePair<string, JsonNode>> SortedNodes;
    public List<KeyValuePair<string, JsonNode>?> Nodes;

    public SortedJson(JsonSortConfig config)
    {
        Config = config;
    }

    public async Task<JsonObject> Sort(JsonNode json)
    {
        var newJson = new JsonObject();

        var jsonNodes = json.AsObject().ToList();
        IniNodesList();

        foreach (var keyValuePair in jsonNodes)
        {
            var name = keyValuePair.Key;
            var value = keyValuePair.Value;

            var index = Config.NodesOrder.FindIndex(x => x == name);
            if (index == -1)
            {
                Nodes.Add(keyValuePair);
            }
            else
            {
                Nodes[index] = keyValuePair;
            }
        }

        foreach (var keyValuePair in Nodes)
        {
            if(keyValuePair is null)
                continue;

            newJson.Add(keyValuePair.Value.Key, keyValuePair.Value.Value.CopyNode());
        }

        return newJson;
    }

    private void IniNodesList()
    {
        Nodes = new List<KeyValuePair<string, JsonNode>?>(Config.NodesOrder.Capacity);

        // Fill null values to reserve indexes for properties
        for (var i = 0; i < Config.NodesOrder.Count; i++)
        {
            Nodes.Add(null);
        }
    }
}

public class JsonRelatedFile
{
    public JsonSortConfig Config;

    public IFile File;

    public JsonNode ParsedNode;

    public JsonRelatedFile(JsonSortConfig config, IFile file, JsonNode parsedNode)
    {
        Config = config;
        File = file;
        ParsedNode = parsedNode;
    }

    public async Task Sort()
    {
        var sortedJson = new SortedJson(Config);
        var newJson = await sortedJson.Sort(ParsedNode);

        var newJsonString = newJson.ToJsonString(new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });

        await File.WriteAsync(newJsonString);
    }
}
