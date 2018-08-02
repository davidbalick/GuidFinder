using GuidFinder.Properties;
using System;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Timers;
using WellNet.Sql;
using WellNet.Utils;

namespace GuidFinder
{
    public class ViewModel : INotifyPropertyChanged
    {
        private ConnectionManager _connMgr;
        public event PropertyChangedEventHandler PropertyChanged;

        #region Propertys
        public string[] ConnectionNames { get { return _connMgr.Keys.ToArray(); } }

        private string _status;
        public string Status
        {
            get { return _status; }
            set
            {
                if (value.Equals(_status))
                    return;
                _status = value;
                OnPropertyChanged("Status");
            }
        }

        private string _currentAction;
        public string CurrentAction
        {
            get { return _currentAction; }
            set
            {
                if (value.Equals(_currentAction))
                    return;
                _currentAction = value;
                IsWaiting = CurrentAction.Equals("Look");
                OnPropertyChanged("CurrentAction");
            }
        }

        private bool _isWaiting;
        public bool IsWaiting
        {
            get { return _isWaiting; }
            set
            {
                _isWaiting = value;
                OnPropertyChanged("IsWaiting");
            }
        }

        private string _tableFilter;
        public string TableFilter
        {
            get { return _tableFilter; }
            set
            {
                if (value.Equals(_tableFilter))
                    return;
                _tableFilter = value;
                OnPropertyChanged("TableFilter");
            }
        }

        private string _columnFilter;
        public string ColumnFilter
        {
            get { return _columnFilter; }
            set
            {
                if (value.Equals(_columnFilter))
                    return;
                _columnFilter = value;
                OnPropertyChanged("ColumnFilter");
            }
        }

        private TimeSpan _elapsedTimeSpan;
        private TimeSpan ElapsedTimeSpan
        {
            get { return _elapsedTimeSpan; }
            set
            {
                _elapsedTimeSpan = value;
                ElapsedString = UiLibrary.TimeElapsed(_elapsedTimeSpan, false);
            }
        }
        private string _elapsedString;
        public string ElapsedString
        {
            get { return _elapsedString; }
            set
            {
                _elapsedString = value;
                OnPropertyChanged("ElapsedString");
            }
        }

        private int _statusPercent;
        public int StatusPercent
        {
            get { return _statusPercent; }
            set
            {
                if (value.Equals(_statusPercent))
                    return;
                _statusPercent = value;
                OnPropertyChanged("StatusPercent");
            }
        }

        private string _connectionName = null;
        public string ConnectionName
        {
            get { return _connectionName; }
            set
            {
                if (string.IsNullOrEmpty(value) || value.Equals(_connectionName ?? string.Empty))
                    return;
                _connectionName = value;
                OnPropertyChanged("ConnectionName");
            }
        }

        private string _guidString;
        public string GuidString
        {
            get { return _guidString; }
            set
            {
                _guidString = value;
                OnPropertyChanged("Guid");
            }
        }

        private DataView _findings;
        public DataView Findings
        {
            get { return _findings; }
            set
            {
                _findings = value;
                OnPropertyChanged("Findings");
            }
        }
        #endregion Properties

        public RelayCommand LookCommand { get; set; }
        private BackgroundWorker _bgWorker;
        private Timer _elapsedTimer;

        public ViewModel()
        {
            _connMgr = ConnectionManager.Create();
            LookCommand = new RelayCommand(LookExecuteMethod, LookCanExecuteMethod);
            _bgWorker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            _bgWorker.ProgressChanged += _bgWorker_ProgressChanged;
            _bgWorker.DoWork += _bgWorker_DoWork;
            _bgWorker.RunWorkerCompleted += _bgWorker_RunWorkerCompleted;
            _elapsedTimer = new Timer { Interval = 1000 };
            _elapsedTimer.Elapsed += _elapsedTimer_Elapsed;
            CurrentAction = "Look";
        }

        private void _elapsedTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ElapsedTimeSpan = ElapsedTimeSpan.Add(new TimeSpan(0, 0, 1));
        }

        private bool LookCanExecuteMethod(object arg)
        {
            return !string.IsNullOrEmpty(ConnectionName) && !string.IsNullOrEmpty(GuidString);
        }

        private void LookExecuteMethod(object obj)
        {
            if (_bgWorker.IsBusy)
            {
                _bgWorker.CancelAsync();
            }
            else
            {
                CurrentAction = "Cancel";
                Findings = null;
                var result = new DataTable();
                result.Columns.Add("Table", typeof(string));
                result.Columns.Add("Column", typeof(string));
                Findings = result.DefaultView;
                ElapsedTimeSpan = new TimeSpan(0);
                _elapsedTimer.Start();
                _bgWorker.RunWorkerAsync();
            }
        }

        #region BgWorker
        private void _bgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Guid guid;
            if (!Guid.TryParse(GuidString, out guid))
                throw new Exception("Bad Guid");
            const string TABLENAME = "@TableName";
            const string COLUMNNAME = "@ColumnName";
            var sqlHelper = SqlHelper.Create(_connMgr[ConnectionName]);
            var cmd = sqlHelper.CreateTextCommand(Resources.ColumnsWithGuidsSql);
            var columnListDataTable = sqlHelper.PopulateDataTable(cmd);
            var numTables = columnListDataTable.Rows.Count;
            var rowNum = 0;
            foreach (DataRow dataRow in columnListDataTable.Rows)
            {
                if (_bgWorker.CancellationPending)
                    throw new Exception("Cancelled");
                var tableName = dataRow["TableName"].ToString();
                var columnName = dataRow["ColumnName"].ToString();
                if (!string.IsNullOrEmpty(TableFilter) && !tableName.ToUpper().Contains(TableFilter.ToUpper()))
                    continue;
                if (!string.IsNullOrEmpty(ColumnFilter) && !columnName.ToUpper().Contains(ColumnFilter.ToUpper()))
                    continue;

                var progressInfo = new ProgressInfo
                {
                    CheckedTable = tableName,
                    CheckedColumn = columnName
                };

                var sql = Resources.LookForGuidSql.Replace(TABLENAME, tableName).Replace(COLUMNNAME, columnName);
                cmd = sqlHelper.CreateTextCommand(sql);

                cmd.Parameters.AddWithValue("@Guid", guid);
                var check1ColTab = sqlHelper.PopulateDataTable(cmd);
                progressInfo.Found = check1ColTab.Rows.Count > 0;
                _bgWorker.ReportProgress(rowNum++ * 100 / numTables, progressInfo);
            }
        }

        private void _bgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            StatusPercent = e.ProgressPercentage;
            var progressInfo = e.UserState as ProgressInfo;
            Status = progressInfo.CheckedTabCol;
            if (progressInfo.Found)
            {
                var row = Findings.Table.NewRow();
                row[0] = progressInfo.CheckedTable;
                row[1] = progressInfo.CheckedColumn;
                Findings.Table.Rows.Add(row);
            }
        }

        private void _bgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            CurrentAction = "Look";
            _elapsedTimer.Stop();
            if (e.Error != null)
            {
                Status = e.Error.Message;
                return;
            }
            Status = "Completed";
        }
        #endregion BgWorker

        private void OnPropertyChanged(string propName)
        {
            if (PropertyChanged == null)
                return;
            PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }
    class ProgressInfo
    {
        public int Progress { get; set; }
        public string CheckedTable { get; set; }
        public string CheckedColumn { get; set; }
        public bool Found { get; set; }
        public string CheckedTabCol
        {
            get { return string.Format("{0}.{1}...", CheckedTable, CheckedColumn); }
        }

    }
}
