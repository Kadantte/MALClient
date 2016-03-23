﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.StartScreen;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALClient.Comm;
using MALClient.Items;
using MALClient.Pages;
using MALClient.UserControls;

namespace MALClient.ViewModels
{
    public class AnimeSeason
    {
        public string Name;
        public string Url;
    }

    public class AnimeListViewModel : ViewModelBase
    {
        private readonly ObservableCollection<AnimeItemAbstraction> _animeItemsSet =
            new ObservableCollection<AnimeItemAbstraction>(); //All for current list

        private List<AnimeItemAbstraction> _allLoadedAnimeItems = new List<AnimeItemAbstraction>();
        private List<AnimeItemAbstraction> _allLoadedAuthAnimeItems = new List<AnimeItemAbstraction>();
        private List<AnimeItemAbstraction> _allLoadedAuthMangaItems = new List<AnimeItemAbstraction>();

        private List<AnimeItemAbstraction> _allLoadedMangaItems = new List<AnimeItemAbstraction>();

        private List<AnimeItemAbstraction> _allLoadedSeasonalAnimeItems = new List<AnimeItemAbstraction>();

        private int _allPages;

        public ObservableCollection<PivotItem> _animePages = new ObservableCollection<PivotItem>();

        private bool _initiazlized;

        private int _itemsPerPage = Settings.ItemsPerPage;
        private string _prevListSource;


        private AnimeListWorkModes _prevWorkMode = AnimeListWorkModes.Anime;

        private SortOptions _sortOption = SortOptions.SortNothing;
        private bool _wasPreviousQuery;
        public AnimeSeason CurrentSeason;
        public ObservableCollection<PivotItem> AnimePages => _animePages;

        public ObservableCollection<ListViewItem> SeasonSelection { get; } = new ObservableCollection<ListViewItem>();

        public SortOptions SortOption
        {
            get { return _sortOption; }
            set
            {
                _sortOption = value;
                CurrentPage = 1;
            }
        }

        public int CurrentStatus => GetDesiredStatus();

        public async void Init(AnimeListPageNavigationArgs args)
        {
            //base
            _initiazlized = false;
            NavMgr.ResetBackNav();

            //take out trash
            _animeItemsSet.Clear();
            _animePages = new ObservableCollection<PivotItem>();
            RaisePropertyChanged(() => AnimePages);

            //give visual feedback
            Loading = true;
            await Task.Delay(1);

            //depending on args
            var gotArgs = false;
            if (args != null) //Save current mode
            {
                WorkMode = args.WorkMode;
                if (args.NavArgs) // Use args if we have any
                {
                    ListSource = args.ListSource;
                    SortDescending = SortDescending = args.Descending;
                    SetSortOrder(args.SortOption); //index
                    SetDesiredStatus(args.Status);
                    CurrentPage = args.CurrPage;
                    CurrentSeason = args.CurrSeason;
                    gotArgs = true;
                }
            }
            else //assume default AnimeList
            {
                WorkMode = AnimeListWorkModes.Anime;
            }

            switch (WorkMode)
            {
                case AnimeListWorkModes.Manga:
                case AnimeListWorkModes.Anime:
                    if (!gotArgs)
                        SetDefaults();

                    AppBtnListSourceVisibility = true;
                    AppbarBtnPinTileVisibility = Visibility.Collapsed;

                    if (WorkMode == AnimeListWorkModes.Anime)
                    {
                        SortAirDayVisibility = Visibility.Visible;
                        Sort3Label = "Watched";
                        StatusAllLabel = "All";
                        Filter1Label = "Watching";
                        Filter5Label = "Plan to watch";
                    }
                    else // manga
                    {
                        SortAirDayVisibility = Visibility.Collapsed;
                        Sort3Label = "Read";
                        StatusAllLabel = "All";
                        Filter1Label = "Reading";
                        Filter5Label = "Plan to read";
                    }

                    //try to set list source - display notice on fail
                    if (string.IsNullOrWhiteSpace(ListSource))
                    {
                        if (!string.IsNullOrWhiteSpace(Creditentials.UserName))
                            ListSource = Creditentials.UserName;
                    }
                    if (string.IsNullOrWhiteSpace(ListSource))
                    {
                        EmptyNoticeVisibility = true;
                        EmptyNoticeContent =
                            "We have come up empty...\nList source is not set.\nLog in or set it manually.";
                        BtnSetSourceVisibility = true;
                        Loading = false;
                    }
                    else
                        await FetchData(); //we have source we can fetch

                    break;
                case AnimeListWorkModes.SeasonalAnime:
                    NavMgr.RegisterBackNav(PageIndex.PageAnimeList, null);
                    Loading = true;
                    EmptyNoticeVisibility = false;
                    AppbarBtnPinTileVisibility = Visibility.Visible;
                    AppBtnListSourceVisibility = false;
                    AppBtnGoBackToMyListVisibility = Visibility.Collapsed;
                    BtnSetSourceVisibility = false;

                    if (!gotArgs)
                    {
                        SortDescending = false;
                        SetSortOrder(SortOptions.SortWatched); //index
                        SetDesiredStatus((int) AnimeStatus.AllOrAiring);
                        CurrentSeason = null;
                    }
                    SwitchFiltersToSeasonal();
                    SwitchSortingToSeasonal();

                    await FetchSeasonalData();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            View.InitSortOptions(SortOption, SortDescending);
            UpdateUpperStatus();
            _initiazlized = true;
        }

        public async Task RefreshList(bool searchSource = false, bool fakeDelay = false)
        {
            var finished = false;
            await Task.Run(() =>
            {
                var query = ViewModelLocator.Main.CurrentSearchQuery;
                var queryCondition = !string.IsNullOrWhiteSpace(query) && query.Length > 1;
                if (!_wasPreviousQuery && searchSource && !queryCondition)
                    // refresh was requested from search but there's nothing to update
                {
                    finished = true;
                    return;
                }

                _wasPreviousQuery = queryCondition;

                _animeItemsSet.Clear();
                var status = queryCondition ? 7 : GetDesiredStatus();

                IEnumerable<AnimeItemAbstraction> items;
                switch (WorkMode)
                {
                    case AnimeListWorkModes.Anime:
                        items = _allLoadedAnimeItems;
                        break;
                    case AnimeListWorkModes.SeasonalAnime:
                        items = _allLoadedSeasonalAnimeItems;
                        break;
                    case AnimeListWorkModes.Manga:
                        items = _allLoadedMangaItems;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                items = items.Where(item => queryCondition || status == 7 || item.MyStatus == status);

                if (queryCondition)
                    items = items.Where(item => item.Title.ToLower().Contains(query.ToLower()));

                switch (SortOption)
                {
                    case SortOptions.SortTitle:
                        items = items.OrderBy(item => item.Title);
                        break;
                    case SortOptions.SortScore:
                        if (WorkMode != AnimeListWorkModes.SeasonalAnime)
                            items = items.OrderBy(item => item.MyScore);
                        else
                            items = items.OrderBy(item => item.GlobalScore);
                        break;
                    case SortOptions.SortWatched:
                        if (WorkMode == AnimeListWorkModes.SeasonalAnime)
                            items = items.OrderBy(item => item.Index);
                        else
                            items = items.OrderBy(item => item.MyEpisodes);
                        break;
                    case SortOptions.SortNothing:
                        break;
                    case SortOptions.SortAirDay:
                        var today = (int) DateTime.Now.DayOfWeek;
                        today++;
                        var nonAiringItems = items.Where(abstraction => abstraction.AirDay == -1);
                        var airingItems = items.Where(abstraction => abstraction.AirDay != -1);
                        var airingAfterToday = airingItems.Where(abstraction => abstraction.AirDay >= today);
                        var airingBeforeToday = airingItems.Where(abstraction => abstraction.AirDay < today);
                        if (SortDescending)
                            items =
                                airingAfterToday.OrderByDescending(abstraction => today - abstraction.AirDay)
                                    .Concat(
                                        airingBeforeToday.OrderByDescending(abstraction => today - abstraction.AirDay)
                                            .Concat(nonAiringItems));
                        else
                            items =
                                airingBeforeToday.OrderBy(abstraction => today - abstraction.AirDay)
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
                foreach (var item in items)
                    _animeItemsSet.Add(item);
            });
            if (finished)
                return;
            //If we have items then we should hide EmptyNotice       
            EmptyNoticeVisibility = _animeItemsSet.Count == 0;

            //How many pages do we have?
            if (fakeDelay)
                await Task.Delay(10);
            UpdatePageSetup();
            UpdateUpperStatus();
        }

        private void SwitchSortingToSeasonal()
        {
            Sort3Label = "Index";
        }

        private void SwitchFiltersToSeasonal()
        {
            StatusAllLabel = "Airing";
        }

        private async void ReloadList()
        {
            if (WorkMode == AnimeListWorkModes.SeasonalAnime)
                await FetchSeasonalData(true);
            else
                await FetchData(true);
        }

        public void AddAnimeEntry(AnimeItemAbstraction parentAbstraction)
        {
            if (_allLoadedAuthAnimeItems.Count > 0)
            {
                if (parentAbstraction.RepresentsAnime)
                    _allLoadedAuthAnimeItems.Add(parentAbstraction);
                else
                    _allLoadedAuthMangaItems.Add(parentAbstraction);
            }
        }

        public void RemoveAnimeEntry(AnimeItemAbstraction parentAbstraction)
        {
            if (_allLoadedAuthAnimeItems.Count > 0)
            {
                if (parentAbstraction.RepresentsAnime)
                    _allLoadedAuthAnimeItems.Remove(parentAbstraction);
                else
                    _allLoadedAuthMangaItems.Remove(parentAbstraction);
            }
        }

        #region PropertyPairs

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

        public int CurrentPage { get; set; } = 1;

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

        private Visibility _appbarBtnPinTileVisibility;

        public Visibility AppbarBtnPinTileVisibility
        {
            get { return _appbarBtnPinTileVisibility; }
            set
            {
                _appbarBtnPinTileVisibility = value;
                RaisePropertyChanged(() => AppbarBtnPinTileVisibility);
            }
        }

        private bool _appBtnListSourceVisibility = true;

        public bool AppBtnListSourceVisibility
        {
            get { return _appBtnListSourceVisibility; }
            set
            {
                _appBtnListSourceVisibility = value;
                RaisePropertyChanged(() => AppBtnListSourceVisibility);
            }
        }

        private Visibility _appBtnGoBackToMyListVisibility = Visibility.Collapsed;

        public Visibility AppBtnGoBackToMyListVisibility
        {
            get { return _appBtnGoBackToMyListVisibility; }
            set
            {
                _appBtnGoBackToMyListVisibility = value;
                RaisePropertyChanged(() => AppBtnGoBackToMyListVisibility);
            }
        }

        private int _statusSelectorSelectedIndex;

        public int StatusSelectorSelectedIndex
        {
            get { return _statusSelectorSelectedIndex; }
            set
            {
                if (value == _statusSelectorSelectedIndex)
                    return;

                _statusSelectorSelectedIndex = value;
                RaisePropertyChanged(() => StatusSelectorSelectedIndex);
                Loading = true;
                CurrentPage = 1;
                if (_initiazlized)
                    RefreshList(false, true);
            }
        }

        //For hiding/showing header bar - XamlResources/DictionaryAnimeList.xml
        private GridLength _pivotHeaerGridRowHeight = new GridLength(0);

        public GridLength PivotHeaerGridRowHeight
        {
            get { return _pivotHeaerGridRowHeight; }
            set
            {
                _pivotHeaerGridRowHeight = value;
                RaisePropertyChanged(() => PivotHeaerGridRowHeight);
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

        private string _sort3Label = "Watched";

        public string Sort3Label
        {
            get { return _sort3Label; }
            set
            {
                _sort3Label = value;
                RaisePropertyChanged(() => Sort3Label);
            }
        }

        private string _filter1Label = "Watching";

        public string Filter1Label
        {
            get { return _filter1Label; }
            set
            {
                _filter1Label = value;
                RaisePropertyChanged(() => Filter1Label);
            }
        }

        private string _filter5Label = "Plan to watch";

        public string Filter5Label
        {
            get { return _filter5Label; }
            set
            {
                _filter5Label = value;
                RaisePropertyChanged(() => Filter5Label);
            }
        }

        private string _statusAllLabel = "All";

        public string StatusAllLabel
        {
            get { return _statusAllLabel; }
            set
            {
                _statusAllLabel = value;
                RaisePropertyChanged(() => StatusAllLabel);
            }
        }

        private ICommand _refreshCommand;

        public ICommand RefreshCommand
        {
            get
            {
                return _refreshCommand ??
                       (_refreshCommand = new RelayCommand(ReloadList));
            }
        }

        private ICommand _goBackToMyListCommand;

        public ICommand GoBackToMyListCommand
        {
            get
            {
                return _goBackToMyListCommand ??
                       (_goBackToMyListCommand = new RelayCommand(() =>
                       {
                           ListSource = Creditentials.UserName;
                           FetchData();
                       }));
            }
        }

        public AnimeListPage View { get; set; }

        public AnimeListWorkModes WorkMode { get; set; }

        private Visibility _animesPivotHeaderVisibility;

        public Visibility AnimesPivotHeaderVisibility
        {
            get { return _animesPivotHeaderVisibility; }
            set
            {
                _animesPivotHeaderVisibility = value;
                PivotHeaerGridRowHeight = value == Visibility.Collapsed ? new GridLength(0) : new GridLength(40);
                RaisePropertyChanged(() => AnimesPivotHeaderVisibility);
            }
        }

        private Visibility _sortAirDayVisibility;

        public Visibility SortAirDayVisibility
        {
            get { return _sortAirDayVisibility; }
            set
            {
                _sortAirDayVisibility = value;
                RaisePropertyChanged(() => SortAirDayVisibility);
            }
        }

        private int _animesPivotSelectedIndex;

        public int AnimesPivotSelectedIndex
        {
            get { return _animesPivotSelectedIndex; }
            set
            {
                _animesPivotSelectedIndex = value;
                CurrentPage = value + 1;
                //AppbarBtnPinTileIsEnabled = false;
                RaisePropertyChanged(() => AnimesPivotSelectedIndex);
            }
        }

        private int _seasonalUrlsSelectedIndex;

        public int SeasonalUrlsSelectedIndex
        {
            get { return _seasonalUrlsSelectedIndex; }
            set
            {
                if (value == _seasonalUrlsSelectedIndex || value < 0)
                    return;
                _seasonalUrlsSelectedIndex = value;
                CurrentSeason = SeasonSelection[value].Tag as AnimeSeason;
                RaisePropertyChanged(() => SeasonalUrlsSelectedIndex);
                View.FlyoutSeasonSelectionHide();
                CurrentPage = 1;
                FetchSeasonalData();
            }
        }

        public AnimeItem _currentlySelectedAnimeItem;

        public AnimeItem CurrentlySelectedAnimeItem
        {
            get { return _currentlySelectedAnimeItem; }
            set
            {
                _currentlySelectedAnimeItem = value;
                //AppbarBtnPinTileIsEnabled = true;
            }
        }

        public bool _appbarBtnPinTileIsEnabled;

        public bool AppbarBtnPinTileIsEnabled
        {
            get { return _appbarBtnPinTileIsEnabled; }
            set
            {
                _appbarBtnPinTileIsEnabled = value;
                RaisePropertyChanged(() => AppbarBtnPinTileIsEnabled);
            }
        }

        #endregion

        #region Helpers

        public override void Cleanup()
        {
            _animeItemsSet.Clear();
            _animePages = new ObservableCollection<PivotItem>();
            RaisePropertyChanged(() => AnimePages);
            base.Cleanup();
        }

        //private string GetLastUpdatedStatus()
        //{
        //    if (WorkMode == AnimeListWorkModes.SeasonalAnime)
        //        return "";
        //    var output = "Updated ";
        //    try
        //    {
        //        TimeSpan lastUpdateDiff = DateTime.Now.Subtract(_lastUpdate);
        //        if (lastUpdateDiff.Days > 0)
        //            output += lastUpdateDiff.Days + "day" + (lastUpdateDiff.Days > 1 ? "s" : "") + " ago.";
        //        else if (lastUpdateDiff.Hours > 0)
        //        {
        //            output += lastUpdateDiff.Hours + "hour" + (lastUpdateDiff.Hours > 1 ? "s" : "") + " ago.";
        //        }
        //        else if (lastUpdateDiff.Minutes > 0)
        //        {
        //            output += $"{lastUpdateDiff.Minutes} minute" + (lastUpdateDiff.Minutes > 1 ? "s" : "") + " ago.";
        //        }
        //        else
        //        {
        //            output += "just now.";
        //        }
        //        if (lastUpdateDiff.Days < 20000) //Seems like reasonable workaround
        //            UpdateNoticeVisibility = true;
        //    }
        //    catch (Exception)
        //    {
        //        output = "";
        //    }

        //    return output;
        //}

        private void SetSortOrder(SortOptions? option)
        {
            switch (option ?? (WorkMode == AnimeListWorkModes.Manga ? Settings.MangaSortOrder : Settings.AnimeSortOrder)
                )
            {
                case SortOptions.SortNothing:
                    SortOption = SortOptions.SortNothing;
                    break;
                case SortOptions.SortTitle:
                    SortOption = SortOptions.SortTitle;
                    break;
                case SortOptions.SortScore:
                    SortOption = SortOptions.SortScore;
                    break;
                case SortOptions.SortWatched:
                    SortOption = SortOptions.SortWatched;
                    break;
                case SortOptions.SortAirDay:
                    SortOption = SortOptions.SortAirDay;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void SetDefaults()
        {
            SetSortOrder(null);
            SetDesiredStatus(null);
            SortDescending = WorkMode == AnimeListWorkModes.Manga
                ? Settings.IsMangaSortDescending
                : Settings.IsSortDescending;
        }

        public async void UpdateUpperStatus(int retries = 5)
        {
            while (true)
            {
                var page = Utils.GetMainPageInstance();

                if (page != null)

                    if (WorkMode != AnimeListWorkModes.SeasonalAnime)
                        if (!string.IsNullOrWhiteSpace(ListSource))
                            page.CurrentStatus =
                                $"{ListSource} - {Utils.StatusToString(GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";
                        else
                            page.CurrentStatus =
                                $"{(WorkMode == AnimeListWorkModes.Anime ? "Anime list" : "Manga list")}";
                    else
                        page.CurrentStatus =
                            $"{CurrentSeason?.Name} - {Utils.StatusToString(GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";

                else if (retries >= 0)
                {
                    await Task.Delay(1000);
                    retries = retries - 1;
                    continue;
                }
                break;
            }
        }

        #endregion

        #region Pagination

        public bool CanLoadPages;

        public void UpdatePageSetup(bool updatePerPage = false)
        {
            CanLoadPages = false;
            if (updatePerPage) //called from settings
            {
                _itemsPerPage = Settings.ItemsPerPage;
                return;
            }
            var realPage = CurrentPage;
            _allPages = (int) Math.Ceiling((double) _animeItemsSet.Count/_itemsPerPage);
            AnimesPivotHeaderVisibility = _allPages == 1 ? Visibility.Collapsed : Visibility.Visible;
            _animePages = new ObservableCollection<PivotItem>();
            for (var i = 0; i < _allPages; i++)
            {
                AnimePages.Add(new PivotItem
                {
                    Header = $"{i + 1}",
                    Content = new AnimePagePivotContent(_animeItemsSet.Skip(_itemsPerPage*i).Take(_itemsPerPage))
                });
            }

            RaisePropertyChanged(() => AnimePages);
            CanLoadPages = true;
            try
            {
                AnimesPivotSelectedIndex = realPage - 1;
            }
            catch (Exception)
            {
                CurrentPage = 1;
            }
            if (AnimePages.Count > 0)
                (AnimePages[AnimesPivotSelectedIndex].Content as AnimePagePivotContent).LoadContent();
            Loading = false;
        }

        #endregion

        #region FetchAndPopulate

        private async Task FetchSeasonalData(bool force = false)
        {
            Loading = true;
            EmptyNoticeVisibility = false;
            var setDefaultSeason = false;
            if (CurrentSeason == null)
            {
                CurrentSeason = new AnimeSeason {Name = "Airing", Url = "http://myanimelist.net/anime/season"};
                setDefaultSeason = true;
            }
            Utils.GetMainPageInstance().CurrentStatus = "Downloading data...\nThis may take a while...";
            var data = new List<SeasonalAnimeData>();
            await Task.Run(async () => data = await new AnimeSeasonalQuery(CurrentSeason).GetSeasonalAnime(force));
            if (data == null)
            {
                await RefreshList();
                Loading = false;
                return;
            }
            _allLoadedSeasonalAnimeItems = new List<AnimeItemAbstraction>();
            var source = _allLoadedAuthAnimeItems.Count > 0
                ? _allLoadedAuthAnimeItems
                : new List<AnimeItemAbstraction>();
            foreach (var animeData in data)
            {
                try
                {
                    DataCache.RegisterVolatileData(animeData.Id, new VolatileDataCache
                    {
                        DayOfAiring = animeData.AirDay,
                        GlobalScore = animeData.Score,
                        Genres = animeData.Genres
                    });
                    var abstraction = source.FirstOrDefault(item => item.Id == animeData.Id);
                    if (abstraction == null)
                        _allLoadedSeasonalAnimeItems.Add(new AnimeItemAbstraction(animeData));
                    else
                    {
                        abstraction.AirDay = animeData.AirDay;
                        abstraction.GlobalScore = animeData.Score;
                        abstraction.ViewModel.UpdateWithSeasonData(animeData);
                        _allLoadedSeasonalAnimeItems.Add(abstraction);
                    }
                }
                catch (Exception e)
                {
                    // wat
                }
            }

            SeasonSelection.Clear();
            var i = 0;
            var currSeasonIndex = -1;
            foreach (var seasonalUrl in DataCache.SeasonalUrls)
            {
                if (seasonalUrl.Key != "current")
                {
                    SeasonSelection.Add(new ListViewItem
                    {
                        Content = seasonalUrl.Key,
                        Tag = new AnimeSeason {Name = seasonalUrl.Key, Url = seasonalUrl.Value}
                    });
                    i++;
                }
                else
                    currSeasonIndex = Convert.ToInt32(seasonalUrl.Value) - 1;
                if (seasonalUrl.Key == CurrentSeason.Name)
                {
                    _seasonalUrlsSelectedIndex = i - 1;
                    RaisePropertyChanged(() => SeasonalUrlsSelectedIndex);
                }
            }
            //we have set artificial default one because we did not know what lays ahead of us
            if (setDefaultSeason && currSeasonIndex != -1)
            {
                CurrentSeason = SeasonSelection[currSeasonIndex].Tag as AnimeSeason;
                _seasonalUrlsSelectedIndex = currSeasonIndex;
                RaisePropertyChanged(() => SeasonalUrlsSelectedIndex);
            }
            DataCache.SaveVolatileData();

            await RefreshList();
            Loading = false;
        }

        public async Task FetchData(bool force = false)
        {
            if (!force && _prevListSource == ListSource && _prevWorkMode == WorkMode)
            {
                foreach (var item in _allLoadedAnimeItems.Where(abstraction => abstraction.Loaded))
                {
                    item.ViewModel.SignalBackToList();
                }
                await RefreshList();
                return;
            }
            _prevWorkMode = WorkMode;
            _prevListSource = ListSource;

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

            switch (WorkMode)
            {
                case AnimeListWorkModes.Anime:
                    _allLoadedAnimeItems = new List<AnimeItemAbstraction>();
                    if (force)
                        _allLoadedAuthAnimeItems = new List<AnimeItemAbstraction>();
                    else if (_allLoadedAuthAnimeItems.Count > 0 &&
                             string.Equals(ListSource, Creditentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                        _allLoadedAnimeItems = _allLoadedAuthAnimeItems;
                    break;
                case AnimeListWorkModes.Manga:
                    _allLoadedMangaItems = new List<AnimeItemAbstraction>();
                    if (force)
                        _allLoadedAuthMangaItems = new List<AnimeItemAbstraction>();
                    else if (_allLoadedAuthMangaItems.Count > 0 &&
                             string.Equals(ListSource, Creditentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                        _allLoadedMangaItems = _allLoadedAuthMangaItems;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }


            if (WorkMode == AnimeListWorkModes.Anime ? _allLoadedAnimeItems.Count == 0 : _allLoadedMangaItems.Count == 0)
            {
                var possibleCachedData = force ? null : await DataCache.RetrieveDataForUser(ListSource, WorkMode);
                var data = "";
                if (possibleCachedData != null)
                {
                    data = possibleCachedData.Item1;
                    //_lastUpdate = possibleCachedData.Item2;
                }
                else
                {
                    var args = new MalListParameters
                    {
                        Status = "all",
                        Type = WorkMode == AnimeListWorkModes.Anime ? "anime" : "manga",
                        User = ListSource
                    };
                    await Task.Run(async () => data = await new MalListQuery(args).GetRequestResponse());
                    if (string.IsNullOrEmpty(data) || data.Contains("<error>Invalid username</error>"))
                    {
                        //no data?
                        await RefreshList();
                        Loading = false;
                        return;
                    }
                    DataCache.SaveDataForUser(ListSource, data, WorkMode);
                }
                var parsedData = XDocument.Parse(data);
                var auth = Creditentials.Authenticated &&
                           string.Equals(ListSource, Creditentials.UserName, StringComparison.CurrentCultureIgnoreCase);
                switch (WorkMode)
                {
                    case AnimeListWorkModes.Anime:
                        var anime = parsedData.Root.Elements("anime").ToList();
                        foreach (var item in anime)
                            _allLoadedAnimeItems.Add(new AnimeItemAbstraction(auth, item.Element("series_title").Value,
                                item.Element("series_image").Value,
                                Convert.ToInt32(item.Element("series_animedb_id").Value),
                                Convert.ToInt32(item.Element("my_status").Value),
                                Convert.ToInt32(item.Element("my_watched_episodes").Value),
                                Convert.ToInt32(item.Element("series_episodes").Value),
                                Convert.ToInt32(item.Element("my_score").Value)));

                        //_allLoadedAnimeItems = _allLoadedAnimeItems.Distinct().ToList();
                        if (string.Equals(ListSource, Creditentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                            _allLoadedAuthAnimeItems = _allLoadedAnimeItems;
                        break;
                    case AnimeListWorkModes.Manga:
                        var manga = parsedData.Root.Elements("manga").ToList();
                        foreach (var item in manga)
                            _allLoadedMangaItems.Add(new AnimeItemAbstraction(auth, item.Element("series_title").Value,
                                item.Element("series_image").Value,
                                Convert.ToInt32(item.Element("series_mangadb_id").Value),
                                Convert.ToInt32(item.Element("my_status").Value),
                                Convert.ToInt32(item.Element("my_read_chapters").Value),
                                Convert.ToInt32(item.Element("series_chapters").Value),
                                Convert.ToInt32(item.Element("my_score").Value),
                                Convert.ToInt32(item.Element("my_read_volumes").Value),
                                Convert.ToInt32(item.Element("series_volumes").Value)));

                        //_allLoadedMangaItems = _allLoadedMangaItems.Distinct().ToList();
                        if (string.Equals(ListSource, Creditentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                            _allLoadedAuthMangaItems = _allLoadedMangaItems;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            AppBtnGoBackToMyListVisibility = Creditentials.Authenticated &&
                                             !string.Equals(ListSource, Creditentials.UserName,
                                                 StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            await RefreshList();
        }

        public bool TryRetrieveAuthenticatedAnimeItem(int id, ref IAnimeData reference, bool anime = true)
        {
            if (!Creditentials.Authenticated)
                return false;
            try
            {
                reference = anime
                    ? _allLoadedAuthAnimeItems.First(abstraction => abstraction.Id == id).ViewModel
                    : _allLoadedAuthMangaItems.First(abstraction => abstraction.Id == id).ViewModel;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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
            value = value ??
                    (WorkMode == AnimeListWorkModes.Manga ? Settings.DefaultMangaFilter : Settings.DefaultAnimeFilter);

            value = value == 6 || value == 7 ? value - 1 : value;
            value--;

            StatusSelectorSelectedIndex = (int) value;
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

        #region LogInOut

        public void LogOut()
        {
            _animeItemsSet.Clear();
            _animePages = new ObservableCollection<PivotItem>();
            RaisePropertyChanged(() => AnimePages);
            _allLoadedAnimeItems = new List<AnimeItemAbstraction>();
            _allLoadedAuthAnimeItems = new List<AnimeItemAbstraction>();
            _allLoadedMangaItems = new List<AnimeItemAbstraction>();
            _allLoadedAuthMangaItems = new List<AnimeItemAbstraction>();
            _allLoadedSeasonalAnimeItems = new List<AnimeItemAbstraction>();

            ListSource = string.Empty;
            _prevListSource = "";
        }

        public void LogIn()
        {
            _animeItemsSet.Clear();
            _animePages = new ObservableCollection<PivotItem>();
            RaisePropertyChanged(() => AnimePages);
            _allLoadedAnimeItems = new List<AnimeItemAbstraction>();
            _allLoadedAuthAnimeItems = new List<AnimeItemAbstraction>();
            _allLoadedMangaItems = new List<AnimeItemAbstraction>();
            _allLoadedAuthMangaItems = new List<AnimeItemAbstraction>();
            _allLoadedSeasonalAnimeItems = new List<AnimeItemAbstraction>();
            ListSource = Creditentials.UserName;
            _prevListSource = "";
        }

        #endregion
    }
}