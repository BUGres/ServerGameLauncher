// #define TEST

using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Policy;
using System.Windows.Threading;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string weburi = "http://127.0.0.1:8088"; // 服务器
        const string gamepath = "game/yxhm.exe"; // 游戏启动路径
        const string midfilepath = "63d72051e901c069f8aa1b32aa0c43bb"; // 签名前中间文件

        #region 轮播
        /// <summary>
        /// 活动公告咨询三个中的哪一个
        /// </summary>
        int textnewsat = 0;
        /// <summary>
        /// 时间回调实现所需的组件
        /// </summary>
        DispatcherTimer timer = new DispatcherTimer();
        /// <summary>
        /// 新闻数据
        /// </summary>
        List<Dictionary<string, string>> newsTextDict = new List<Dictionary<string, string>>
        {
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        };
        /// <summary>
        /// 轮播图数据
        /// </summary>
        Dictionary<int, string> loopdata = new Dictionary<int, string>();
        /// <summary>
        /// 只在主线程中使用的图片缓存
        /// 因为BitmapImage只能在主线程（UI线程）中使用（大量测试的教训）
        /// </summary>
        Dictionary<string, BitmapImage> bitmapCache = new Dictionary<string, BitmapImage>();
        /// <summary>
        /// 轮播图跳转到uri
        /// </summary>
        Dictionary<int, string> loopuri = new Dictionary<int, string>();
        /// <summary>
        /// 轮播图片循环当前在哪里
        /// </summary>
        int loopat = 0;

        // 能否下载：用户是否同意
        readonly object canDownload = new object();
        bool candownload = false;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            loopat++;
            if (loopat == loopdata.Count) loopat = 0;
            UpdateImage(loopat);
        }
        /// <summary>
        /// 轮播图向右
        /// </summary>
        private void LoopNext()
        {
            if (loopdata.Count == 0) return;
            loopat++;
            if (loopat == loopdata.Count) loopat = 0;
            UpdateImage(loopat);
        }
        /// <summary>
        /// 轮播图向左
        /// </summary>
        private void LoopForward()
        {
            if (loopdata.Count == 0) return;
            loopat--;
            if (loopat == -1) loopat = loopdata.Count - 1;
            UpdateImage(loopat);
        }
        /// <summary>
        /// 更新轮播图
        /// </summary>
        /// <param name="index"></param>
        private void UpdateImage(int index)
        {
            BitmapImage bi;
            if (bitmapCache.ContainsKey(loopdata[index]))
            {
                bi = bitmapCache[loopdata[index]];
            }
            else
            {
                bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(loopdata[index]);
                bi.EndInit();

                bitmapCache[loopdata[index]] = bi;
            }
            img_looping.Source = bi;
        }
        /// <summary>
        /// 轮播中鼠标在轮播图上的悬浮变化使用的代码
        /// </summary>
        DispatcherTimer looptimer = new DispatcherTimer();
        #endregion

        #region DllImport
        [DllImport("FileBuild.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool C_CheckFile(string path, string md5);

        public static string md5(string input)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    builder.Append(data[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        #endregion

        #region 开始前

        /// <summary>
        /// 用于存储下载的清单
        /// </summary>
        Dictionary<string, bool> DownloadCheck = new Dictionary<string, bool>();

        /// <summary>
        /// 单个要下载的文件的类
        /// </summary>
        class WebFile
        {
            public string path;
            public long length;
            public string hash;
            public int part;

            public bool isFinish;
        }
        
        /// <summary>
        /// 7z相关的字符串操作必要的函数
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static string GetStr(int i)
        {
            if (i < 10) return "00" + i;
            else if (i < 100) return "0" + i;
            else return i.ToString();
        }

        /// <summary>
        /// 启动7z相关的进程，用于解压
        /// </summary>
        /// <param name="sevenZipPath"></param>
        /// <param name="archivePath"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        static string ExtractArchive(string sevenZipPath, string archivePath, string outputDir)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = " x " + archivePath,// $"x \"{archivePath}\" -o\"{outputDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return "无法启动 7-Zip 进程.";
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return string.Empty;// "解压成功\n" + output;
                    }
                    else
                    {
                        return $"解压失败\n错误信息:\n{error}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"发生异常: {ex.Message}";
            }
        }

        /// <summary>
        /// 转移文件，这里从hash路径文件转为正常文件
        /// </summary>
        /// <param name="oldFilePath"></param>
        /// <param name="newFilePath"></param>
        /// <returns></returns>
        static string MoveAndRenameFile(string oldFilePath, string newFilePath)
        {
            try
            {
                // 检查原文件是否存在
                if (!System.IO.File.Exists(oldFilePath))
                {
                    return "原文件不存在.";
                }

                // 提取新文件路径的目录
                string newDirectory = System.IO.Path.GetDirectoryName(newFilePath);

                // 如果目标目录不存在，则创建目录
                if (!Directory.Exists(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);
                }

                // 移动并重命名文件
                System.IO.File.Move(oldFilePath, newFilePath);

                return "文件移动并重命名成功.";
            }
            catch (Exception ex)
            {
                return $"发生异常: {ex.Message}";
            }
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            // 鼠标拖动看函数 Grid_MouseLeftButtonDown

            Thread initThread = new Thread(Init);
            initThread.Start();

            Thread downloadThread = new Thread(DownLoad);
            downloadThread.Start();
        }

        /// <summary>
        /// 初始化菜单线程
        /// </summary>
        public async void Init()
        {
            string content = "";

        InitStart:
            try
            {
                HttpClient client = new HttpClient();
                var response = await client.GetAsync(weburi + "/official/news.config");
                content = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                // 没有获取到，这里是客户端没有联网或者其他原因
                await Task.Delay(1000);
                goto InitStart;
            }

            // 轮播图数据下载，咨询选项等下载
            string config = content;
            var strs = config.Split('\n');
            int countoftext = 0;
            for (int i = 0; i < strs.Length; i++)
            {
                string str = strs[i];
                if (str.Split(' ')[0] == "image")
                {
                    loopdata.Add(i, str.Split(' ')[1]);
                    loopuri.Add(i, str.Split(' ')[2]);
                }
                else
                {
                    // newsTextDict
                    newsTextDict[countoftext / 3].Add(str.Split(' ')[1], str.Split(' ')[2]);
                    countoftext++;
                }
            }

            await Application.Current.Dispatcher.BeginInvoke(() => {
                var Timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3), // 轮播时间设置
                };
                Timer.Tick += OnTimerTick;
                Timer.Start();
                timer = Timer;
                OnTimerTick(null, null);
                btn_huodong_Click(null, null);
            });

            // 新闻网页等，已经加载完毕，接下来做下载相关的内容
            return;
        }

        /// <summary>
        /// 下载线程
        /// </summary>
        public async void DownLoad()
        {
            Application.Current.Dispatcher.Invoke(() => {
                lab_download.Content = "下载文件清单..";
            });

            #region 清单下载build.config

            string content = "";

        InitStart:
            try
            {
                HttpClient client = new HttpClient();
                var response = await client.GetAsync(weburi + "/download/build.config");
                content = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                // 没有获取到，这里是客户端没有联网或者其他原因
                await Task.Delay(1000);
                goto InitStart;
            }

            foreach (var str in content.Split('\n'))
            {
                if (string.IsNullOrEmpty(str)) continue;
                DownloadCheck.Add(str, false);
            }

            #endregion

            Application.Current.Dispatcher.Invoke(() => {
                lab_download.Content = "校验留存内容..";
            });

            #region 校验存留文件

            List<WebFile> files = new List<WebFile>();
            foreach (var item in DownloadCheck)
            {
                // 可能有路径含有空格，这里就得处理一下
                var strs_s = item.Key.Split(' ');
                string[] strs = new string[4];

                strs[0] = "";
                for (int j = 0; j < strs_s.Length - 3; j++)
                {
                    strs[0] += strs_s[j] + " ";
                }

                strs[0] = strs[0].Substring(0, strs[0].Length - 1);
                strs[1] = strs_s[strs_s.Length - 3];
                strs[2] = strs_s[strs_s.Length - 2];
                strs[3] = strs_s[strs_s.Length - 1];
                WebFile f = new WebFile();
                f.path = strs[0];
                f.length = long.Parse(strs[1]);
                f.hash = strs[2];
                f.part = int.Parse(strs[3]);
                files.Add(f);
            }

            long totalbyte = 0; // 总计byte
            long retainbyte = 0; // 保有的正确文件byte
            long lackbyte = 0; // 缺少的byte
            bool allokay = true; // 校验通过了吗

            // 资源校验，通过则不加载
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                bool isCheckOkay = true;
                try
                {
                    isCheckOkay = C_CheckFile(Encoding.Default.GetString(Encoding.Default.GetBytes(file.path)), Encoding.Default.GetString(Encoding.Default.GetBytes(file.hash)));
                }
                catch
                {
                    isCheckOkay = false;
                }
                if (isCheckOkay)
                {
                    file.isFinish = true;
                    retainbyte += file.length;
                }
                else
                {
                    // 检测没过，说明文件被修改了！！，必须删除旧的文件
                    try
                    {
                        if (Directory.Exists(file.path))
                        { 
                            Directory.Delete(file.path, true);
                        }
                    }
                    catch 
                    {
                        MessageBox.Show("文件占用中：" + file.path);
                        Process.GetCurrentProcess().Kill(); // 有文件是修改的且有占用，必须闪退
                    }

                    file.isFinish = false;
                    allokay = false;
                    lackbyte += file.length;
                }
                totalbyte += file.length;
            }

            // MessageBox.Show(retainbyte + " + " + lackbyte + " = " + totalbyte + " = " + (retainbyte + lackbyte));
            #endregion

            #region 下载/更新/补全 游戏
            if (!allokay)
            {
                // 创建下载缓存文件
                if (Directory.Exists("download"))
                {
                    Directory.Delete("download", true);
                }
                Directory.CreateDirectory("download");
                if (Directory.Exists(midfilepath))
                {
                    Directory.Delete(midfilepath, true);
                }
                Directory.CreateDirectory(midfilepath);

                // 下载中的文件片统计（每个片差不多大小，所以只显示文件片个数表示下载进度）
                int okaycount = 0;
                int allcount = 0;
                foreach (var afile in files)
                {
                    if (afile.isFinish) okaycount += afile.part;
                    allcount += afile.part;
                }

                foreach (var file in files)
                {
                    if (file.isFinish)
                    {
                        continue;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        int p = (int)((float)okaycount / (float)allcount * 100);
                        lab_download.Content = "下载文件" + p + "%";
                    });

                    // 先进行分片下载
                    for (int si = 0; si < file.part; si++)
                    {
                        string path = "download/" + md5(file.path) + ".7z." + GetStr(si + 1);
                        {
                        DownloadStart:
                            try
                            {
                                using (HttpClient client = new HttpClient())
                                {
                                    HttpResponseMessage response = await client.GetAsync(weburi + "/" + path);
                                    response.EnsureSuccessStatusCode();
                                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                                    await System.IO.File.WriteAllBytesAsync(path, fileBytes);
                                }
                            }
                            catch (Exception e)
                            {
                                // MessageBox.Show(e.Message + "\n" + weburi + "/" + path);
                                goto DownloadStart;
                            }
                        }
                        okaycount++;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        int p = (int)((float)okaycount / (float)allcount * 100);
                        lab_download.Content = "安装文件" + p + "%";
                    });

                    // 再进行下载后片的安装
                    string spath = "download/" + md5(file.path) + ".7z." + GetStr(1);
                    string result = ExtractArchive("7z.exe", spath, "");
                    if (result != string.Empty)
                    {
                        MessageBox.Show(result);
                        Process.GetCurrentProcess().Kill(); // 7z那边传来了解压失败，这里只能闪退了，日志用户应该能看见（上面的messagebox）
                    }

                    // 下载后的文件我无条件相信，所以这里直接从hashPathFile转为实际File
                    var paths = file.path.Split('\\');
                    var hashpath = "";
                    for (int stri = 0; stri < paths.Length; stri++)
                    {
                        hashpath += md5(paths[stri]) + "\\";
                    }
                    hashpath = hashpath.Substring(0, hashpath.Length - 1);
                    MoveAndRenameFile(hashpath, file.path);
                }

                if (Directory.Exists("download"))
                {
                    Directory.Delete("download", true);
                }
                if (Directory.Exists(midfilepath))
                {
                    Directory.Delete(midfilepath, true);
                }
            }

            #endregion

            Application.Current.Dispatcher.Invoke(() => {
                lab_download.Content = "      进入游戏";
            });
        }

        #region WPF自动代码

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void StartButton_MouseEnter(object sender, MouseEventArgs e)
        {
            img_enter.Opacity = 1;
            img_enterB.Opacity = 0;
        }

        private void StartButton_MouseLeave(object sender, MouseEventArgs e)
        {
            img_enter.Opacity = 0;
            img_enterB.Opacity = 1;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (lab_download.Content as string == "      进入游戏")
            {
                Process p = new Process();
                p.StartInfo.FileName = gamepath;
                p.Start();
            }
        }

        private void btn_close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btn_loop_Click(object sender, RoutedEventArgs e)
        {
            if (loopuri.ContainsKey(loopat))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = loopuri[loopat],
                    UseShellExecute = true // 确保使用默认浏览器打开链接
                });
            }
        }

        void Update_Add(object? sender , EventArgs e)
        {
            double alpha = img_tureleft.Opacity;
            alpha += 0.05;
            if (alpha >= 0.5) alpha = 0.5;
            img_tureleft.Opacity = alpha;
            img_tureright.Opacity = alpha;
        }

        void Update_Sub(object? sender, EventArgs e)
        {
            double alpha = img_tureleft.Opacity;
            alpha -= 0.05;
            if (alpha <= 0) alpha = 0;
            img_tureleft.Opacity = alpha;
            img_tureright.Opacity = alpha;
        }

        private void btn_loop_enter(object sender, RoutedEventArgs e)
        {
            looptimer.Stop();
            var Timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.02), // 轮播时间设置
            };
            Timer.Tick += Update_Add;
            Timer.Start();
            looptimer = Timer;
            timer.Stop();
        }

        private void btn_loop_leave(object sender, RoutedEventArgs e)
        {
            looptimer.Stop();
            var Timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.02), // 轮播时间设置
            };
            Timer.Tick += Update_Sub;
            Timer.Start();
            looptimer = Timer;
            timer.Start();
        }

        private void btn_tureright_Click(object sender, RoutedEventArgs e)
        {
            LoopNext();
        }

        private void btn_tureleft_Click(object sender, RoutedEventArgs e)
        {
            LoopForward();
        }

        // 咨询悬浮效果-文字变色
        private void btn_info_enter(object sender, MouseEventArgs e)
        {
            if (sender == btn_info1)
            {
                lab_info1.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
            else if (sender == btn_info2)
            {
                lab_info2.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
            else if (sender == btn_info3)
            {
                lab_info3.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
            else if (sender == btn_huodong)
            {
                lab_huodong.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
            else if (sender == btn_gonggao)
            {
                lab_gonggao.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
            else if (sender == btn_zixvn)
            {
                lab_zixvn.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 0));
            }
        }
        // 咨询悬浮效果-文字变色
        private void btn_info_leave(object sender, MouseEventArgs e)
        {
            if (sender == btn_info1)
            {
                lab_info1.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (sender == btn_info2)
            {
                lab_info2.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (sender == btn_info3)
            {
                lab_info3.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (sender == btn_huodong)
            {
                lab_huodong.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (sender == btn_gonggao)
            {
                lab_gonggao.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            else if (sender == btn_zixvn)
            {
                lab_zixvn.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
        }

        // 活动
        private void btn_huodong_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            textnewsat = 0;
            WebSelect.Margin = new Thickness(54, 487, 824, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        // 公告
        private void btn_gonggao_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            textnewsat = 1;
            WebSelect.Margin = new Thickness(114, 487, 764, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        // 咨讯
        private void btn_zixvn_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            textnewsat = 2;
            WebSelect.Margin = new Thickness(174, 487, 704, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        private void btn_info1_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(0).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }

        private void btn_info2_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(1).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }

        private void btn_info3_Click(object sender, RoutedEventArgs e)
        {
            if (newsTextDict[0].Count < 3 || newsTextDict[1].Count < 3 || newsTextDict[2].Count < 3)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(2).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }
        #endregion
    }
}