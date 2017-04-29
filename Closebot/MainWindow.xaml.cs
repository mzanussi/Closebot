using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TwitterClient;

namespace Closebot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Twitter keys.
        private const string TWITTER_KEYS = @"C:\Temp\Closebot-data\twitterkeys";
        private const string QUANDL_KEYS = @"C:\Temp\Closebot-data\quandlkeys";
        private string kAccessToken;
        private string kAccessTokenSecret;
        private string kConsumerKey;
        private string kConsumerSecret;
        private string kQuandlKey;
        //private string kLastClose;

        private bool kRetry = false;

        // Twitter API.
        // courtesy of:
        // https://gist.github.com/sdesalas/c82b92200816ecc83af1
        private static API twitter;

        // log4net
        private static readonly ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // A timer and its interval.
        private DispatcherTimer timer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();

            log.Info("*** Closebot launched ***");

            if (Initialize())
            {
                // enable buttons
                btnStart.IsEnabled = true;
                btnUpdate.IsEnabled = true;
                btnStart.Focus();
                // timer setup
                timer.Tick += new EventHandler(dispatchTimer_Tick);
                timer.Interval = new TimeSpan(0, 1, 0);
            }
        }

        /// <summary>
        /// Read in the Quandl key file ('quandlkey') and read in
        /// the Twitter key file ('twitterkeys'). Instantiate the
        /// Twitter client API.
        /// </summary>
        private bool Initialize()
        {
            try
            {
                // The Quandl key file consists of the following information, each on its own line:
                //   API key
                string[] keys = File.ReadAllLines(QUANDL_KEYS);
                kQuandlKey = keys[0];
                log.Info(String.Format("Quandl keys loaded successfully from {0}.", QUANDL_KEYS));
            }
            catch (FileNotFoundException)
            {
                log.Error(String.Format("FileNotFoundException: Cannot load Quandl key file {0}.", QUANDL_KEYS));
                tbStatus.Text = "Cannot load Quandl key file.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }
            catch (Exception)
            {
                log.Error(String.Format("Exception: Problem encountered loading Quandl keys {0}.", QUANDL_KEYS));
                tbStatus.Text = "Cannot load Quandl keys, problem with key file.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            try
            {
                // The Twitter key file consists of the following information, each on its own line:
                //   access token
                //   access token secret
                //   consumer key (API key)
                //   consumer secret (API secret)
                string[] keys = File.ReadAllLines(TWITTER_KEYS);
                kAccessToken = keys[0];
                kAccessTokenSecret = keys[1];
                kConsumerKey = keys[2];
                kConsumerSecret = keys[3];
                log.Info(String.Format("Twitter keys loaded successfully from {0}.", TWITTER_KEYS));
                // instantiate TwitterClient api
                // TEMP::
                //twitter = new API(accessToken, accessTokenSecret, consumerKey, consumerSecret);
            }
            catch (FileNotFoundException)
            {
                log.Error(String.Format("FileNotFoundException: Cannot load Twitter key file {0}.", TWITTER_KEYS));
                tbStatus.Text = "Cannot load Twitter key file.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }
            catch (Exception)
            {
                log.Error(String.Format("Exception: Problem encountered loading Twitter keys {0}.", TWITTER_KEYS));
                tbStatus.Text = "Cannot load Twitter keys, problem with key file.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method runs every minute and attempts to retrieve market data
        /// from Quandl and tweet it out, under two circumstances:
        /// 1) It's 2:30pm on a weekday, or,
        /// 2) The global variable kRetry is set to True.
        /// </summary>
        private void dispatchTimer_Tick(object sender, EventArgs e)
        {
            // is it 2:30pm on a week day? or are we in retry state?
            if ((DateTime.Now.DayOfWeek != DayOfWeek.Saturday && 
                DateTime.Now.DayOfWeek != DayOfWeek.Sunday &&
                DateTime.Now.Hour == 14 && DateTime.Now.Minute == 30) || kRetry)
            {
                // kRetry is True when we reach 2:30pm but Quandl hasn't updated
                // stock market index close prices yet; this will allow this portion
                // of code to execute after 2:30pm for a set amount of time retrying
                // Quandl for data. kRetry can also be True if tweets are unsuccessful.
                // Note that kRetry can only be True when app is running; that is,
                // the app will never start with kRetry in a True state.
                if (kRetry)
                {
                    // Quandl data wasn't updated on last attempt. If time is within
                    // 9 hours of our first attempt, try again at the quarter hour or
                    // quarter till hour. If we are outside the timeframe, stop retries.
                    if (DateTime.Now.Hour < 23 && (DateTime.Now.Minute == 15 || DateTime.Now.Minute == 45))
                    {
                        lblLastTick.Content = "Retry @ " + DateTime.Now;
                        log.Info("RETRY Hour: " + DateTime.Now.Hour + ", Minute: " + DateTime.Now.Minute);
                        // Tweet out market data.
                        bool success = TweetMarketData();
                        log.Info("RETRY Success = " + success);
                        // If tweet was successful, go ahead and turn off retries.
                        if (success == true)
                        {
                            kRetry = false;
                            log.Info(String.Format("Retry is now {0}.", kRetry));
                            lblRetry.Visibility = Visibility.Hidden;
                        }
                    }
                    // It's been over 9 hours of retrying with no success, so go ahead
                    // and turn off retries. This day will be skipped.
                    else if (DateTime.Now.Hour >= 23 || DateTime.Now.Hour < 14 || (DateTime.Now.Hour == 14 && DateTime.Now.Minute < 30))
                    {
                        lblLastTick.Content = "Rstop @ " + DateTime.Now;
                        log.Info("Quandl hasn't updated market data 9 hours since close; ignore today.");
                        kRetry = false;
                        log.Info(String.Format("Retry is now {0}.", kRetry));
                        lblRetry.Visibility = Visibility.Hidden;
                    }
                }
                // The current time has hit 2:30pm on a M-F, so run an initial check
                // to see if Quandl has today's market data posted.
                else
                {
                    lblLastTick.Content = "Init @ " + DateTime.Now;
                    log.Info("INIT Hour: " + DateTime.Now.Hour + ", Minute: " + DateTime.Now.Minute);
                    // Tweet out market data.
                    bool success = TweetMarketData();
                    log.Info("INIT Success = " + success);
                    // If not successful (whether no Quandl data or 
                    // issues with Twitter) activate retries.
                    if (success == false)
                    {
                        kRetry = true;
                        log.Info(String.Format("Retry set to {0}.", kRetry));
                        lblRetry.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieve market data from Quandl and return it as a JSON string.
        /// </summary>
        private string Quandl(string index, string date, string key)
        {
            // DJI:  https://www.quandl.com/api/v3/datasets/YAHOO/INDEX_DJI.json?api_key={key}&start_date=2017-02-23
            // GSPC: https://www.quandl.com/api/v3/datasets/YAHOO/INDEX_GSPC.json?api_key={key}&start_date=2017-02-23
            // IXIC: https://www.quandl.com/api/v3/datasets/YAHOO/INDEX_IXIC.json?api_key={key}&start_date=2017-02-23

            // TEMP::
            // build api call from index and date
            string apiUrl = String.Format("https://www.quandl.com/api/v3/datasets/YAHOO/{0}.json?api_key={1}&start_date={2}&end_date={3}", index, key, date, date);

            // Create the request object.
            var request = (HttpWebRequest)WebRequest.Create(apiUrl);
            request.Method = "GET";
            //request.ContentType = "application/x-www-form-urlencoded";
            //request.Headers.Add("Authorization", "bearer " + token.access_token);
            //request.Accept = "application/json, text/javascript, */*; q=0.01";

            int statusCode;
            string statusDescription;
            string responseBody;

            try
            {
                // Make the request.
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    // StatusCode is an enumeration, so you can cast the 
                    // result to an integer to get the numeric StatusCode.
                    int numericStatusCode = (int)response.StatusCode;
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        // Failure
                        log.Error(String.Format("API Request failed in CallAPI(). Received HTTP {0}.", response.StatusCode));
                        tbStatus.Text = String.Format("API Request failed. Received HTTP {0}.", response.StatusCode);
                        tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                        return null;
                    }

                    // Success
                    var responseValue = String.Empty;
                    using (var responseStream = response.GetResponseStream())
                    {
                        // Check for null content.
                        if (responseStream == null)
                        {
                            return null;
                        }
                        using (var reader = new StreamReader(responseStream))
                        {
                            responseValue = reader.ReadToEnd();
                            // Check for empty content.
                            if (String.IsNullOrEmpty(responseValue))
                            {
                                return null;
                            }
                        }
                    }
                    statusCode = numericStatusCode;
                    statusDescription = response.StatusCode.ToString();
                    responseBody = responseValue;
                }

                if (!string.IsNullOrEmpty(responseBody))
                {
                    // return the Json data
                    return responseBody;
                }

                // temp only; 
                //lblMessage.ForeColor = Color.Green;
                //lblMessage.Text = String.Format("API Request SUCCESS. Received HTTP {0}.", statusDescription);
            }
            catch (WebException ex)
            {
                log.Error("WebException in CallAPI(): " + ex.Message);
                log.Error(ex.StackTrace);
                log.Error("Is the network down?");
                tbStatus.Text = ex.Message;
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
            catch (Exception ex)
            {
                log.Error("Exception in CallAPI(): " + ex.Message);
                log.Error(ex.StackTrace);
                tbStatus.Text = ex.Message;
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }

            return null;
        }

        /// <summary>
        /// Calls the Quandl API to return market data for a specific index and date.
        /// The data is returned as a tuple (date, open, close).
        /// </summary>
        private (string date, decimal open, decimal close) GetMarketData(string index, string date)
        {
            // Call the Quandl API and get the market data.
            //string json = Quandl(index, date, kQuandlKey);

            // Testing: (API call result in file)
            string[] aJson = File.ReadAllLines(@"C:\Temp\Closebot-data\ex_null.json");
            string json = aJson[0];

            log.Info("JSON: " + json);

            if (!string.IsNullOrEmpty(json))
            {
                ResultSet rs = ParseJson(json);

                if (rs != null)
                {
                    if (rs.dataset.data.Count > 0)
                    {
                        string cdate = null;
                        decimal open = 0.0M;
                        decimal close = 0.0M;
                        log.Info("dataset data count = " + rs.dataset.data.Count);
                        // Quandl API call is for a single day, so grab the only element. 
                        // (Note: if there were multiple days, the first element is
                        // always the most recent data.)
                        List<object> o = rs.dataset.data.ElementAt<List<object>>(0);
                        // Use the index of the "Date", "Open", and "Close" 
                        // columns in column_names to retrieve the respective 
                        // values from data.
                        int i = 0;
                        foreach (string column in rs.dataset.column_names)
                        {
                            switch (column)
                            {
                                case "Date":
                                    cdate = (string)o.ElementAt(i);
                                    break;
                                case "Open":
                                    open = (decimal)o.ElementAt(i);
                                    break;
                                case "Close":
                                    close = (decimal)o.ElementAt(i);
                                    break;
                            }
                            i++;
                        }
                        log.Info(index + " on " + cdate);
                        log.Info(index + " open: " + open);
                        log.Info(index + " close: " + close);

                        return (date: cdate, open: open, close: close);
                    }
                    else
                    {
                        log.Info(String.Format("The {0} dataset contains no market data.", index));
                        tbStatus.Text = String.Format("The {0} dataset contains no market data.", index);
                        tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
                else
                {
                    log.Info(String.Format("The {0} ResultSet is null.", index));
                    tbStatus.Text += String.Format("\nThe {0} ResultSet is null.", index);
                    tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            else
            {
                log.Info(String.Format("The {0} JSON is null or empty.", index));
                tbStatus.Text += String.Format("\nThe {0} JSON is null or empty.", index);
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }

            return (date: null, open: 0.0M, close: 0.0M);
        }

        /// <summary>
        /// Given a Quandl JSON response, return the ResultSet. 
        /// </summary>
        private ResultSet ParseJson(string json)
        {
            ResultSet resultSet = null;
            try
            {
                JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
                resultSet = javaScriptSerializer.Deserialize<ResultSet>(json);
            }
            catch (ArgumentException ex)
            {
                log.Error("ArgumentException in ParseJson(): " + ex.Message);
                log.Error(ex.StackTrace);
                tbStatus.Text = ex.Message;
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
            catch (InvalidOperationException ex)
            {
                log.Error("InvalidOperationException in ParseJson(): " + ex.Message);
                log.Error(ex.StackTrace);
                tbStatus.Text = ex.Message;
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
            catch (Exception ex)
            {
                log.Error("Exception in ParseJson(): " + ex.Message);
                log.Error(ex.StackTrace);
                tbStatus.Text = ex.Message;
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
            }

            return resultSet;
        }

        /// <summary>
        /// Fetch market data, tweet it, update GUI.
        /// </summary>
        private bool TweetMarketData()
        {
            // TESTING TICK ONLY, REMOVE ME
            //return false;
            lblCloseDate.Content = DateTime.Now.ToString("yyyy-MM-dd");
            log.Info(String.Format("Close Date is {0}.", DateTime.Now.ToString("yyyy-MM-dd")));

            // If last close date (as stored in config) is empty
            // or last close date < today, proceed with routine.
            // Otherwise, return now.
            //if (!String.IsNullOrEmpty(kLastClose))
            //{
            //    DateTime lc = DateTime.ParseExact(kLastClose, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            //    DateTime dt = DateTime.ParseExact(GetTodaysDate(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            //    if (lc >= dt)
            //    {
            //        log.Info("Last close date >= today's date; nothing to do here");
            //        return false;
            //    }
            //}

            var gspc = GetMarketData("INDEX_GSPC", DateTime.Now.ToString("yyyy-MM-dd"));

            if (gspc.date == null)
            {
                log.Info("The INDEX_GSPC data is not available.");
                tbStatus.Text += "\nS&P 500 data is not available. See logs for more info.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            var dji = GetMarketData("INDEX_DJI", DateTime.Now.ToString("yyyy-MM-dd"));

            if (dji.date == null)
            {
                log.Info("The INDEX_DJI data is not available.");
                tbStatus.Text += "\nDJIA data is not available. See logs for more info.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            var ixic = GetMarketData("INDEX_IXIC", DateTime.Now.ToString("yyyy-MM-dd"));

            if (ixic.date == null)
            {
                log.Info("The INDEX_IXIC data is not available.");
                tbStatus.Text += "\nNASDAQ data is not available. See logs for more info.";
                tbStatus.Foreground = new SolidColorBrush(Colors.Red);
                return false;
            }

            lblSPX.Content = gspc.close.ToString("0.##");
            lblDJIA.Content = dji.close.ToString("0.##");
            lblNASDAQ.Content = ixic.close.ToString("0.##");

            //if (foo == null)
            //{
            //    return;
            //}
            //tweetText = tweetText + t1 + "\n";

            // process the resultset (i.e. tweet it)
            //tbTweet.Text = "12/31/2017 U.S. Market close\n$DJIA 20523.32 +23.99 (+0.05%)\n$SPX 2933.82 -0.59 (-0.01%)\n$NASDAQ 5682.01 +5.12 (+0.12%)";
            // assuming a successful tweet, save to config
            //kLastClose = GetTodaysDate();
            //Closebot.Properties.Settings.Default.LastCloseDate = kLastClose;
            //Closebot.Properties.Settings.Default.Save();

            return true;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            log.Info("Start button clicked.");
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
            btnUpdate.IsEnabled = false;

            // start the timer
            timer.Start();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            log.Info("Stop button clicked.");
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnUpdate.IsEnabled = true;

            // stop the timer
            timer.Stop();
        }

        private void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = false;
            btnUpdate.IsEnabled = false;

            // Update button, useful when app needs to be stopped prior
            // to market data being updated by Quandl. So that when app
            // is restarted, user can manually try updating market data.
            bool status = TweetMarketData();

            // update last tick lable to show Manual Update @datetime
            lblLastTick.Content = "Manual @ " + DateTime.Now;

            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnUpdate.IsEnabled = true;
        }
    }
}
