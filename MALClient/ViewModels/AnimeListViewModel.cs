﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using GalaSoft.MvvmLight;
using MALClient.Comm;
using MALClient.Items;
using MALClient.Pages;

namespace MALClient.ViewModels
{
    public class AnimeListViewModel : ViewModelBase
    {
        private List<AnimeItemAbstraction> _allLoadedAnimeItems = new List<AnimeItemAbstraction>();

        private readonly ObservableCollection<AnimeItem> _animeItems = new ObservableCollection<AnimeItem>(); // + Page

        private readonly ObservableCollection<AnimeItemAbstraction> _animeItemsSet =
            new ObservableCollection<AnimeItemAbstraction>(); //All for current list

        private readonly int _itemsPerPage = Utils.GetItemsPerPage();
        private int _allPages;


        private DateTime _lastUpdate;
        private Timer _timer;
        private bool _loaded;
        private bool _seasonalState;
        private bool _wasPreviousQuery;

        public SortOptions SortOption { get; private set; } = SortOptions.SortNothing;

        public int CurrentStatus => GetDesiredStatus();
        public string CurrentUpdateStatus => GetLastUpdatedStatus();         
        public string CurrentPageStatus => $"{_currentPage}/{_allPages}";


        private string _listSource;
        public string ListSource
        {
            get { return _listSource; }
            set
            {
                _listSource = value;
                RaisePropertyChanged(() => ListSource);
            }
        }

        private string _emptyNoticeContent;
        public string EmptyNoticeContent
        {
            get { return _emptyNoticeContent; }
            set
            {
                _emptyNoticeContent = value;
                RaisePropertyChanged(() => EmptyNoticeContent);
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get { return _currentPage; }
            set
            {
                _currentPage = value;
                RaisePropertyChanged(() => CurrentPageStatus);
            }
        }

        private bool _emptyNoticeVisibility;
        public bool EmptyNoticeVisibility
        {
            get { return _emptyNoticeVisibility; }
            set
            {
                _emptyNoticeVisibility = value;
                RaisePropertyChanged(() => EmptyNoticeVisibility);
            }
        }

        private bool _animesTopPageControlsVisibility;
        public bool AnimesTopPageControlsVisibility
        {
            get { return _animesTopPageControlsVisibility; }
            set
            {
                _animesTopPageControlsVisibility = value;
                RaisePropertyChanged(() => AnimesTopPageControlsVisibility);
            }
        }

        private bool _updateNoticeVisibility;
        public bool UpdateNoticeVisibility
        {
            get { return _updateNoticeVisibility; }
            set
            {
                _updateNoticeVisibility = value;
                RaisePropertyChanged(() => UpdateNoticeVisibility);
            }
        }

        private bool _btnSetSourceVisibility;
        public bool BtnSetSourceVisibility
        {
            get { return _btnSetSourceVisibility; }
            set
            {
                _btnSetSourceVisibility = value;
                RaisePropertyChanged(() => BtnSetSourceVisibility);
            }
        }

        private bool _appbarBtnPinTileVisibility;
        public bool AppbarBtnPinTileVisibility
        {
            get { return _appbarBtnPinTileVisibility; }
            set
            {
                _appbarBtnPinTileVisibility = value;
                RaisePropertyChanged(() => AppbarBtnPinTileVisibility);
            }
        }

        private bool _appBtnListSourceVisibility;
        public bool AppBtnListSourceVisibility
        {
            get { return _appBtnListSourceVisibility; }
            set
            {
                _appBtnListSourceVisibility = value;
                RaisePropertyChanged(() => AppBtnListSourceVisibility);
            }
        }

        private bool _prevPageButtonEnableState;
        public bool PrevPageButtonEnableState
        {
            get { return _prevPageButtonEnableState; }
            set
            {
                _prevPageButtonEnableState = value;
                RaisePropertyChanged(() => PrevPageButtonEnableState);
            }
        }

        private bool _nextPageButtonEnableState;
        public bool NextPageButtonEnableState
        {
            get { return _nextPageButtonEnableState; }
            set
            {
                _nextPageButtonEnableState = value;
                RaisePropertyChanged(() => NextPageButtonEnableState);
            }
        }        

        private int _statusSelectorSelectedIndex;
        public int StatusSelectorSelectedIndex
        {
            get { return _statusSelectorSelectedIndex; }
            set
            {
                _statusSelectorSelectedIndex = value;
                RaisePropertyChanged(() => StatusSelectorSelectedIndex);
            }
        }

        private bool _loading;
        public bool Loading
        {
            get { return _loading; }
            set
            {
                _loading = value;
                RaisePropertyChanged(() => Loading);
            }
        }

        private bool _sortDescending;
        public bool SortDescending
        {
            get { return _sortDescending; }
            set
            {
                _sortDescending = value;
                RaisePropertyChanged(() => SortDescending);
            }
        }

        private AnimeListPage _view;

        public AnimeListPage View
        {
            get { return _view; }
            set
            {
                _view = value;
                //Init
            }
        }

        public async void Init(AnimeListPageNavigationArgs args)
        {
            if (args != null)
            {
                if (args.LoadSeasonal)
                {
                    _seasonalState = true;
                    Loading = true;
                    EmptyNoticeVisibility = false;
                    AppbarBtnPinTileVisibility = false;
                    AppBtnListSourceVisibility = false;

                    if (args.NavArgs)
                    {
                        ListSource = args.ListSource;
                        SortDescending = SortDescending = args.Descending;
                        SetSortOrder(args.SortOption); //index
                        SetDesiredStatus(args.Status);
                        CurrentPage = args.CurrPage;
                    }
                    else
                    {
                        BtnOrderDescending.IsChecked = SortDescending = false;
                        SetSortOrder(SortOptions.SortWatched); //index
                        SetDesiredStatus((int)AnimeStatus.AllOrAiring);
                    }

                    SwitchFiltersToSeasonal();
                    SwitchSortingToSeasonal();

                    await Task.Run(async () =>
                    {
                        await
                            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High,
                                async () => { await FetchSeasonalData(); });
                    });
                    return;
                } // else we just have nav data

                TxtListSource.Text = args.ListSource;
                _currentSoure = args.ListSource;
                SetSortOrder(args.SortOption);
                SetDesiredStatus(args.Status);
                BtnOrderDescending.IsChecked = args.Descending;
                SortDescending = args.Descending;
                CurrentPage = args.CurrPage;
            }
            else // default
                SetDefaults();

            if (string.IsNullOrWhiteSpace(ListSource))
            {
                if (!string.IsNullOrWhiteSpace(Creditentials.UserName))
                    TxtListSource.Text = Creditentials.UserName;
            }
            _currentSoure = TxtListSource.Text;
            if (string.IsNullOrWhiteSpace(ListSource))
            {
                EmptyNotice.Visibility = Visibility.Visible;
                EmptyNotice.Text += "\nList source is not set.\nLog in or set it manually.";
                BtnSetSource.Visibility = Visibility.Visible;
                UpdateUpperStatus();
            }
            else
            {
                await FetchData();
            }

            if (_timer == null)
                _timer = new Timer(state => { UpdateStatus(); }, null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds,
                    (int)TimeSpan.FromMinutes(1).TotalMilliseconds);

            UpdateStatus();
        }

        private void SetSortOrder(SortOptions? option)
        {
            switch (option ?? Utils.GetSortOrder())
            {
                case SortOptions.SortNothing:
                    SortOption = SortOptions.SortNothing;
                    sort4.IsChecked = true;
                    break;
                case SortOptions.SortTitle:
                    SortOption = SortOptions.SortTitle;
                    sort1.IsChecked = true;
                    break;
                case SortOptions.SortScore:
                    SortOption = SortOptions.SortScore;
                    sort2.IsChecked = true;
                    break;
                case SortOptions.SortWatched:
                    SortOption = SortOptions.SortWatched;
                    sort3.IsChecked = true;
                    break;
                case SortOptions.SortAirDay:
                    SortOption = SortOptions.SortAirDay;
                    sort5.IsChecked = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RefreshList(bool searchSource = false)
        {
            var query = ViewModelLocator.Main.CurrentSearchQuery;
            var queryCondition = !string.IsNullOrWhiteSpace(query) && query.Length > 1;
            if (!_wasPreviousQuery && searchSource && !queryCondition)
                // refresh was requested from search but there's nothing to update
                return;

            _wasPreviousQuery = queryCondition;
            _currentPage = 1;

            _animeItemsSet.Clear();
            var status = queryCondition ? 7 : GetDesiredStatus();

            IEnumerable<AnimeItemAbstraction> items =
                _allLoadedAnimeItems.Where(item => queryCondition || status == 7 || item.MyStatus == status);
            if (queryCondition)
                items = items.Where(item => item.Title.ToLower().Contains(query.ToLower()));
            switch (SortOption)
            {
                case SortOptions.SortTitle:
                    items = items.OrderBy(item => item.Title);
                    break;
                case SortOptions.SortScore:
                    if (!_seasonalState)
                        items = items.OrderBy(item => item.MyScore);
                    else
                        items = items.OrderBy(item => item.GlobalScore);
                    break;
                case SortOptions.SortWatched:
                    if (_seasonalState)
                        items = items.OrderBy(item => item.Index);
                    else
                        items = items.OrderBy(item => item.MyEpisodes);
                    break;
                case SortOptions.SortNothing:
                    break;
                case SortOptions.SortAirDay:
                    var today = (int)DateTime.Now.DayOfWeek;
                    today++;
                    IEnumerable<AnimeItemAbstraction> nonAiringItems =
                        items.Where(abstraction => abstraction.AirDay == -1);
                    IEnumerable<AnimeItemAbstraction> airingItems = items.Where(abstraction => abstraction.AirDay != -1);
                    IEnumerable<AnimeItemAbstraction> airingAfterToday =
                        airingItems.Where(abstraction => abstraction.AirDay >= today);
                    IEnumerable<AnimeItemAbstraction> airingBeforeToday =
                        airingItems.Where(abstraction => abstraction.AirDay < today);
                    if (SortDescending)
                        items = airingAfterToday.OrderByDescending(abstraction => today - abstraction.AirDay)
                            .Concat(
                                airingBeforeToday.OrderByDescending(abstraction => today - abstraction.AirDay)
                                    .Concat(nonAiringItems));
                    else
                        items = airingBeforeToday.OrderBy(abstraction => today - abstraction.AirDay)
                            .Concat(
                                airingAfterToday.OrderBy(abstraction => today - abstraction.AirDay)
                                    .Concat(nonAiringItems));

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(SortOption), SortOption, null);
            }
            //If we are descending then reverse order
            if (SortDescending && SortOption != SortOptions.SortAirDay)
                items = items.Reverse();
            //Add all abstractions to current set (spread across pages)
            foreach (AnimeItemAbstraction item in items)
                _animeItemsSet.Add(item);
            //If we have items then we should hide EmptyNotice       
            EmptyNoticeVisibility = _animeItemsSet.Count == 0;

            //How many pages do we have?
            _allPages = (int)Math.Ceiling((double)_animeItemsSet.Count / _itemsPerPage);
            if (_allPages <= 1)
                AnimesTopPageControlsVisibility = false;
            else
            {
                AnimesTopPageControlsVisibility = true;
                if (CurrentPage <= 1)
                {
                    PrevPageButtonEnableState = false;
                    CurrentPage = 1;
                }
                else
                {
                    PrevPageButtonEnableState = true;
                }

                NextPageButtonEnableState = CurrentPage != _allPages;
            }


            ApplyCurrentPage();
            AlternateRowColors();
            UpdateUpperStatus();
            RaisePropertyChanged(() => CurrentUpdateStatus);
        }

        public async void UpdateUpperStatus(int retries = 5)
        {
            while (true)
            {
                MainViewModel page = Utils.GetMainPageInstance();

                if (page != null)

                    if (!_seasonalState)
                        if (!string.IsNullOrWhiteSpace(ListSource))
                            page.CurrentStatus = $"{ListSource} - {Utils.StatusToString(GetDesiredStatus())}";
                        else
                            page.CurrentStatus = "Anime list";
                    else
                        page.CurrentStatus = $"Airing - {Utils.StatusToString(GetDesiredStatus())}";

                else if (retries >= 0)
                {
                    await Task.Delay(1000);
                    retries = retries - 1;
                    continue;
                }
                break;
            }
        }

        private void AlternateRowColors()
        {
            for (var i = 0; i < _animeItems.Count; i++)
            {
                _animeItems[i].Setbackground(
                    new SolidColorBrush((i + 1) % 2 == 0 ? Color.FromArgb(170, 230, 230, 230) : Colors.Transparent));
            }
        }

        #region Pagination

        private void PrevPage(object sender, RoutedEventArgs e)
        {
            CurrentPage--;
            PrevPageButtonEnableState = CurrentPage != 1;
            NextPageButtonEnableState = true;
            ApplyCurrentPage();
        }

        private void NextPage(object sender, RoutedEventArgs e)
        {
            CurrentPage++;
            NextPageButtonEnableState = CurrentPage != _allPages;
            PrevPageButtonEnableState = true;
            ApplyCurrentPage();
        }

        private void ApplyCurrentPage()
        {
            _animeItems.Clear();
            foreach (
                AnimeItemAbstraction item in _animeItemsSet.Skip(_itemsPerPage * (CurrentPage - 1)).Take(_itemsPerPage))
                _animeItems.Add(item.AnimeItem);
            RaisePropertyChanged(() => CurrentPageStatus);
        }

        #endregion

        #region FetchAndPopulate

        private async Task FetchSeasonalData(bool force = false)
        {
            List<AnimeItemAbstraction> possibleLoadedData = force
                ? new List<AnimeItemAbstraction>()
                : Utils.GetMainPageInstance().RetrieveSeasonData();
            if (possibleLoadedData.Count == 0)
            {
                Utils.GetMainPageInstance().CurrentStatus = "Downloading data...\nThis may take a while...";
                List<SeasonalAnimeData> data = await new AnimeSeasonalQuery().GetSeasonalAnime(force);
                if (data == null)
                {
                    RefreshList();
                    Loading = false;
                    return;
                }
                _allLoadedAnimeItems.Clear();
                AnimeUserCache loadedStuff = Utils.GetMainPageInstance().RetrieveLoadedAnime();
                Dictionary<int, AnimeItemAbstraction> loadedItems =
                    loadedStuff?.LoadedAnime.ToDictionary(item => item.Id);
                foreach (SeasonalAnimeData animeData in data)
                {
                    DataCache.RegisterVolatileData(animeData.Id, new VolatileDataCache
                    {
                        DayOfAiring = animeData.AirDay,
                        GlobalScore = animeData.Score
                    });
                    _allLoadedAnimeItems.Add(new AnimeItemAbstraction(animeData, loadedItems));
                }
                DataCache.SaveVolatileData();
                Utils.GetMainPageInstance().SaveSeasonData(_allLoadedAnimeItems);
            }
            else
            {
                _allLoadedAnimeItems = possibleLoadedData;
            }

            UpdateUpperStatus();           
            RefreshList();
            Loading = false;
        }

        private string GetLastUpdatedStatus()
        {
            var output = "Updated ";
            try
            {
                TimeSpan lastUpdateDiff = DateTime.Now.Subtract(_lastUpdate);
                if (lastUpdateDiff.Days > 0)
                    output += lastUpdateDiff.Days + "day" + (lastUpdateDiff.Days > 1 ? "s" : "") + " ago.";
                else if (lastUpdateDiff.Hours > 0)
                {
                    output += lastUpdateDiff.Hours + "hour" + (lastUpdateDiff.Hours > 1 ? "s" : "") + " ago.";
                }
                else if (lastUpdateDiff.Minutes > 0)
                {
                    output += $"{lastUpdateDiff.Minutes} minute" + (lastUpdateDiff.Minutes > 1 ? "s" : "") + " ago.";
                }
                else
                {
                    output += "just now.";
                }
                if (lastUpdateDiff.Days < 20000) //Seems like reasonable workaround
                    UpdateNoticeVisibility = true;
            }
            catch (Exception)
            {
                output = "";
            }

            return output;
        }

        private async Task FetchData(bool force = false)
        {
            Loading = true;

            BtnSetSourceVisibility = false;            
            EmptyNoticeVisibility = false;

            if (string.IsNullOrWhiteSpace(ListSource))
            {
                EmptyNoticeVisibility = true;
                EmptyNoticeContent = "We have come up empty...\nList source is not set.\nLog in or set it manually.";
                BtnSetSourceVisibility = true;
            }
            else
            {
                EmptyNoticeContent = "We have come up empty...";
            }

            _allLoadedAnimeItems = new List<AnimeItemAbstraction>();
            _animeItems.Clear();

            if (!force)
                Utils.GetMainPageInstance()
                    .RetrieveAnimeEntries(ListSource, out _allLoadedAnimeItems, out _lastUpdate);

            if (_allLoadedAnimeItems.Count == 0)
            {
                Tuple<string, DateTime> possibleCachedData = force
                    ? null
                    : await DataCache.RetrieveDataForUser(ListSource);
                string data;
                if (possibleCachedData != null)
                {
                    data = possibleCachedData.Item1;
                    _lastUpdate = possibleCachedData.Item2;
                }
                else
                {
                    var args = new AnimeListParameters
                    {
                        status = "all",
                        type = "anime",
                        user = ListSource
                    };
                    data = await new AnimeListQuery(args).GetRequestResponse();
                    if (string.IsNullOrEmpty(data) || data.Contains("<error>Invalid username</error>"))
                    {
                        RefreshList();
                        Loading = false;
                        return;
                    }
                    DataCache.SaveDataForUser(ListSource, data);
                    _lastUpdate = DateTime.Now;
                }
                XDocument parsedData = XDocument.Parse(data);
                List<XElement> anime = parsedData.Root.Elements("anime").ToList();
                var auth = Creditentials.Authenticated &&
                           string.Equals(ListSource, Creditentials.UserName,
                               StringComparison.CurrentCultureIgnoreCase);
                foreach (XElement item in anime)
                {
                    _allLoadedAnimeItems.Add(new AnimeItemAbstraction(
                        auth,
                        item.Element("series_title").Value,
                        item.Element("series_image").Value,
                        Convert.ToInt32(item.Element("series_animedb_id").Value),
                        Convert.ToInt32(item.Element("my_status").Value),
                        Convert.ToInt32(item.Element("my_watched_episodes").Value),
                        Convert.ToInt32(item.Element("series_episodes").Value),
                        Convert.ToInt32(item.Element("my_score").Value)));
                }

                _allLoadedAnimeItems = _allLoadedAnimeItems.Distinct().ToList();

                Utils.GetMainPageInstance().SaveAnimeEntries(ListSource, _allLoadedAnimeItems, _lastUpdate);
            }


            RefreshList();
            //UpdateStatusCounterBadges();
            Loading = false;
        }



        #endregion


        #region StatusRelatedStuff

        private int GetDesiredStatus()
        {
            var value = StatusSelectorSelectedIndex;
            value++;
            return value == 0 ? 1 : value == 5 || value == 6 ? value + 1 : value;
        }

        private void SetDesiredStatus(int? value)
        {
            value = value ?? Utils.GetDefaultAnimeFilter();

            value = value == 6 || value == 7 ? value - 1 : value;
            value--;

            StatusSelectorSelectedIndex = (int)value;
        }

        //private void UpdateStatusCounterBadges()
        //{
        //    Dictionary<int, int> counters = new Dictionary<int, int>();
        //    for (var i = AnimeStatus.Watching; i <= AnimeStatus.PlanToWatch; i++)
        //        counters[(int)i] = 0;
        //    foreach (AnimeItemAbstraction animeItemAbstraction in _allLoadedAnimeItems)
        //    {
        //        if (animeItemAbstraction.MyStatus <= 6)
        //            counters[animeItemAbstraction.MyStatus]++;
        //    }
        //    var j = AnimeStatus.Watching;
        //    foreach (object item in StatusSelector.Items)
        //    {
        //        (item as ListViewItem).Content = counters[(int)j] + " - " + Utils.StatusToString((int)j);
        //        j++;
        //        if ((int)j == 5)
        //            j++;
        //        if (j == AnimeStatus.AllOrAiring)
        //            return;
        //    }
        //}
        #endregion
    }
}