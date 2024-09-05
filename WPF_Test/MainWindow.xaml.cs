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
using System.Windows.Media.Media3D;
using System.Runtime.InteropServices;
using System.CodeDom;
using System.Security.Cryptography;
using static System.Net.WebRequestMethods;

namespace WPF_Test
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string weburi = "http://81.70.22.238:8088"; // 服务器
        const string gamepath = "game/pvzHE-Launcher.exe"; // 游戏启动路径
        const string midfilepath = "63d72051e901c069f8aa1b32aa0c43bb"; // 签名前中间文件

        class WebUpdate
        {
            public bool okay = false; // 更新结束了吗
            public bool result = false; // 更新成功了吗

            public HttpClient client = new HttpClient();
            public string content = string.Empty;
            public readonly object o = new object();
            public async Task Update()
            {
                try
                {
                    // 发送并监听http请求
                    HttpResponseMessage response = await client.GetAsync(weburi + "/official/news.config");
                    response.EnsureSuccessStatusCode();

                    // 响应保存
                    content = await response.Content.ReadAsStringAsync();

                    lock (o)
                    { 
                        okay = true;
                        result = true;
                    }
                    
                }
                catch (Exception e)
                {
#if TEST
                    MessageBox.Show(e.Message);
#endif
                    lock (o)
                    {
                        okay = true;
                        result = false;
                    }
                }
            }
        }

        class DownloadUpdate
        {
            public bool okay = false; // 更新结束了吗
            public bool result = false; // 更新成功了吗

            public HttpClient client = new HttpClient();
            public string content = string.Empty;
            public readonly object o = new object();
//            public async Task Update()
//            {
//                try
//                {
//                    // 发送并监听http请求
//                    HttpResponseMessage response = await client.GetAsync(weburi + "/download/build.config");
//                    response.EnsureSuccessStatusCode();

//                    // 响应保存
//                    content = await response.Content.ReadAsStringAsync();

//                    lock (o)
//                    {
//                        okay = true;
//                        result = true;
//                    }

//                }
//                catch (Exception e)
//                {
//#if TEST
//                    MessageBox.Show(e.Message);
//#endif
//                    lock (o)
//                    {
//                        okay = true;
//                        result = false;
//                    }
//                }
//            }
        }

        // 下面这些是轮播使用的
        readonly object webInit = new object();
        bool isWebInit = false;
        int textnewsat = 0;
        DispatcherTimer timer = new DispatcherTimer();
        List<Dictionary<string, string>> newsTextDict = new List<Dictionary<string, string>> { 
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        }; 
        Dictionary<int, BitmapImage> loopdata = new Dictionary<int, BitmapImage>();
        Dictionary<int, string> loopuri = new Dictionary<int, string>();
        readonly object looplock = new object();
        int at = 0;
        
        // 能否下载：用户是否同意
        readonly object canDownload = new object();
        bool candownload = false;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            at++;
            if (at == loopdata.Count) at = 0;
            UpdateImage(at);
        }

        private void LoopNext()
        {
            at++;
            if (at == loopdata.Count) at = 0;
            UpdateImage(at);
        }

        private void LoopForward()
        {
            at--;
            if (at == -1) at = loopdata.Count - 1;
            UpdateImage(at);
        }

        private void UpdateImage(int index)
        {
            img_looping.Source = loopdata[index];
        }

        // 下载|安装|修补 进度
        float downloadp = 1f;
        float downloadsindelay = 0f;
        // 网络下载更新日志
        readonly object downloadweblock = new object();
        bool downloadweb = false;
        // 进游戏检查通过
        readonly object enterchecklock = new object();
        bool onenter = false;
        // 下载内容校验
        Dictionary<string, bool> DownloadCheck = new Dictionary<string, bool>();
        List<WebFile> FILES = new List<WebFile>();
        bool canentergame = false;

        public BitmapImage CreateDynamicBitmapImage(int width, int height)
        {
            // 创建一个 WriteableBitmap 实例
            var bitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);

            // 创建一个字节数组来保存像素数据
            var pixels = new byte[width * height * 4]; // 4 bytes per pixel (Pbgra32)

            // 填充像素数据，这里设置为渐变色
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4;

                    float xx = (float)x / width;
                    float yy = (float)y / height;

                    if (yy > MathF.Sin(20 * xx + downloadsindelay) / 50 - 1.0f/50 + (1 - downloadp)) // MathF.Sin(xx + downloadsindelay) / 5
                    {
                        pixels[index] = 255;
                        pixels[index + 1] = 255;
                        pixels[index + 2] = 255;
                        pixels[index + 3] = 255;
                    }
                    else
                    {
                        pixels[index] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                        pixels[index + 3] = 0;
                    }
                }
            }

            // 将字节数组写入 WriteableBitmap
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            // 创建 BitmapImage 并将 WriteableBitmap 转换为它
            var bitmapImage = new BitmapImage();
            using (var memoryStream = new MemoryStream())
            {
                // 使用 PngBitmapEncoder 将 WriteableBitmap 编码为 PNG 格式
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(memoryStream);

                // 将内存流设置为 BitmapImage 的源
                memoryStream.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // 冻结以提高性能
            }

            return bitmapImage;
        }

        class WebFile
        {
            public string path;
            public long length;
            public string hash;
            public int part;

            public bool isFinish;
        }

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

        public static string GetStr(int i)
        {
            if (i < 10) return "00" + i;
            else if (i < 100) return "0" + i;
            else return i.ToString();
        }

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

        public MainWindow()
        {
            InitializeComponent();

            #region 鼠标拖动
            this.MouseMove += Draging;
            this.MouseDown += StartDrag;
            this.MouseUp += EndDrag;
            this.MouseLeave += EndDrag;
            #endregion
            #region 从网络上更新所有资源

            WebUpdate wu = new WebUpdate();

            // 从服务器下载news.config
            Thread tUpdate = new Thread(async () => {
                int delay = 100;
                while (true)
                {
                    try
                    {
                        // 发送并监听http请求
                        HttpResponseMessage response = await wu.client.GetAsync(weburi + "/official/news.config");
                        response.EnsureSuccessStatusCode();

                        // 响应保存
                        wu.content = await response.Content.ReadAsStringAsync();
                        lock (wu.o)
                        {
                            wu.okay = true;
                            wu.result = true;
                        }
                        return;
                    }
                    catch (Exception e)
                    {
#if TEST
                        MessageBox.Show(e.Message);
#endif
                        lock (wu.o)
                        {
                            wu.okay = true;
                            wu.result = false;
                        }
                    }
                    await Task.Delay(delay);
                    delay *= 2;
                }
            });
            tUpdate.Start();

            // 通过news.config更新组件
            Thread tTest = new Thread(async () => 
            {
                int delay = 100;
                while (true)
                {
                    // 获取文件 weburl/official/news.config
                    lock (wu.o)
                    {
                        if (wu.okay)
                        {
                            if (wu.result)
                            {
                                lock (webInit)
                                { 
                                    isWebInit = true;
                                }
                                break;
                            }
                        }
                    }
                    await Task.Delay(delay);
                    delay *= 2;
                }
            });
            tTest.Start();


            //BitmapImage bitmap = new BitmapImage();
            //bitmap.BeginInit();
            //bitmap.UriSource = new Uri(weburi + "/loop0.png");
            //bitmap.EndInit();
            //img_looping.Source = bitmap;
            // 这里通过uri覆写所有组件
            while (true)
            {
                lock (webInit)
                {
                    if (isWebInit)
                    {
                        // 到这里所有线程必定已经解放了
                        tTest.Interrupt(); // 这只是个通知
                        tUpdate.Interrupt();

                        // 从此处更新所有图片
                        string config = wu.content;
                        var strs = config.Split('\n');
                        int countoftext = 0;
                        for (int i = 0; i < strs.Length; i++)
                        {
                            string str = strs[i];
                            if (str.Split(' ')[0] == "image")
                            {
                                BitmapImage bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(str.Split(' ')[1]);
                                bitmap.EndInit();
                                lock (webInit)
                                {
                                    loopdata.Add(i, bitmap);
                                    loopuri.Add(i, str.Split(' ')[2]);
                                }
                            }
                            else
                            {
                                // newsTextDict
                                lock (webInit)
                                {
                                    newsTextDict[countoftext / 3].Add(str.Split(' ')[1], str.Split(' ')[2]);
                                }
                                countoftext++;
                            }
                        }
                        lock (looplock)
                        {
                            img_looping.Source = loopdata[at];
                        }
                        break;
                    }
                }
                Task.Delay(50);
            }
            #endregion
            #region 公告初始化
            lab_info1.Content = newsTextDict[0].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[0].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[0].ElementAt(2).Key;
            #endregion
            #region 轮播网页
            var Timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3), // 轮播时间设置
            };
            Timer.Tick += OnTimerTick;
            Timer.Start();
            timer = Timer;
            #endregion
            #region 安装文件检查与校验
            lab_download.Content = "校验\n资源";
            DownloadUpdate du = new DownloadUpdate();

            if (Directory.Exists(midfilepath))
            {
                Directory.Delete(midfilepath, true);
            }

            // 从服务器下载build.config
            Thread tBuild = new Thread(async () =>
            {
                int delay = 100;
                while (true)
                {
                    try
                    {
                        // 发送并监听http请求
                        HttpResponseMessage response = await du.client.GetAsync(weburi + "/download/build.config");
                        response.EnsureSuccessStatusCode();

                        // 响应保存
                        du.content = await response.Content.ReadAsStringAsync();
                        lock (du.o)
                        {
                            du.okay = true;
                            du.result = true;
                        }
                        return;
                    }
                    catch (Exception e)
                    {
#if TEST
                        MessageBox.Show(e.Message);
#endif
                        lock (du.o)
                        {
                            du.okay = true;
                            du.result = false;
                        }
                    }
                    await Task.Delay(delay);
                    delay *= 2;
                }
            });
            tBuild.Start();

            Thread tbuildtest = new Thread(async () =>
            {
                int delay = 100;
                while (true)
                {
                    lock (du.o)
                    {
                        if (du.okay)
                        {
                            if (du.result)
                            {
                                foreach (var str in du.content.Split('\n'))
                                {
                                    if (str == string.Empty) continue;
                                    DownloadCheck.Add(str, false);
                                }
                                lock (downloadweblock)
                                {
                                    downloadweb = true;
                                }
                                break;
                            }
                        }


                    }
                    await Task.Delay(delay);
                    delay *= 2;
                }
            });
            tbuildtest.Start();

            long totalbyte = 0; // 总计byte
            long retainbyte = 0; // 保有的正确文件byte
            long lackbyte = 0; // 缺少的byte
            bool allokay = true; // 校验通过了吗

            Thread tCheck = new Thread(async () =>
            {
                while (true)
                {
                    lock (downloadweblock)
                    {
                        if (downloadweb)
                        {
                            tBuild.Interrupt();
                            tbuildtest.Interrupt();

                            List<WebFile> files = new List<WebFile>();
                            foreach (var item in DownloadCheck)
                            {
                                var strs = item.Key.Split(' ');
                                WebFile f = new WebFile();
                                f.path = strs[0];
                                f.length = long.Parse(strs[1]);
                                f.hash = strs[2];
                                f.part = int.Parse(strs[3]);
                                files.Add(f);
                            }

                            // 资源校验，通过则不加载
                            for (int i = 0; i < files.Count; i++)
                            {
                                var file = files[i];
                                if (C_CheckFile(Encoding.Default.GetString(Encoding.Default.GetBytes(file.path)), Encoding.Default.GetString(Encoding.Default.GetBytes(file.hash))))
                                {
                                    file.isFinish = true;
                                    retainbyte += file.length;
                                }
                                else
                                {
                                    file.isFinish = false;
                                    allokay = false;
                                    lackbyte += file.length;
                                }
                                totalbyte += file.length;
                            }

                            lock (enterchecklock)
                            {
                                onenter = true;
                            }

                            FILES = files;

                            break;
                        }
                    }
                    await Task.Delay(50);
                }
            });
            tCheck.Start();

            Thread tUpdateCheck = new Thread(async () =>
            {
                while (true)
                {
                    lock (enterchecklock)
                    {
                        if (onenter)
                        {
                            tCheck.Interrupt();
                            break;
                        } 
                    }
                    await Task.Delay(50);
                }
                while (true)
                {
                    if (allokay)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            lab_download.Content = "启动\n游戏";
                            // 可以启动块
                            img_EnterGameA.RenderTransform = new RotateTransform() { Angle = 0, CenterX = 64, CenterY = 64 };
                            img_EnterGameB.RenderTransform = new RotateTransform() { Angle = 0, CenterX = 64, CenterY = 64 };
                            DispatcherTimer ta = new DispatcherTimer();
                            ta.Interval = TimeSpan.FromSeconds(0.02);
                            ta.Tick += (object? sender, EventArgs e) =>
                            {
                                (img_EnterGameA.RenderTransform as RotateTransform).Angle += 0.5;
                                (img_EnterGameB.RenderTransform as RotateTransform).Angle += 1.25;
                            };
                            ta.Start();
                            downloadp = 1;
                            canentergame = true;
                        });

                        return;
                    }
                    else
                    {
#if TEST
                        string jiaoyan = "";
                        foreach (var f in FILES)
                        {
                            if (f.isFinish)
                                jiaoyan += f.path + " finish\n";
                            else
                                jiaoyan += f.path + " unfinish\n";
                        }
                        MessageBox.Show(jiaoyan);
#endif
                        Dispatcher.Invoke(() =>
                        {
                            lab_download.Content = "获取\n游戏";
                        });

                        if (!Directory.Exists("download"))
                        {
                            Directory.CreateDirectory("download");
                        }

                        // 下载开始，下载是单线程的
                        long downloadpart = 0;
                        long alldownloadpart = 0; // 要下载的所有片
                        foreach (var f in FILES)
                        {
                            if (f.isFinish) continue;
                            alldownloadpart += f.part;
                        }
                        for (int i = 0; i < FILES.Count; i++)
                        { 
                            var file = FILES[i];
                            if (file.isFinish)
                            {
                                continue;
                            }
#if TEST
                            MessageBox.Show("准备补全文件" + file.path);
#endif
                            long downloadbyte = 0;
                            for (int si = 0; si < file.part; si++)
                            {
                                string spath = "download/" + md5(file.path) + ".7z." + GetStr(si + 1);
                                if (System.IO.File.Exists(spath))
                                {
                                    // MessageBox.Show((file.length / file.part).ToString());
                                    // 如果下载已缓存了
                                    downloadpart++;
                                    Dispatcher.Invoke(() =>
                                    {
                                        downloadp = (float)downloadpart / (float)alldownloadpart;
                                    });
                                    continue;
                                }
                                else
                                {
                                DownloadStart:
                                    await Task.Delay(50);
                                    lock (canDownload)
                                    {
                                        if (!candownload)
                                        {
                                            goto DownloadStart;
                                        } 
                                    }
                                    try
                                    {
                                        using (HttpClient client = new HttpClient())
                                        {
                                            HttpResponseMessage response = await client.GetAsync(weburi + "/" + spath);
                                            response.EnsureSuccessStatusCode();
                                            byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                                            await System.IO.File.WriteAllBytesAsync(spath, fileBytes);
                                            downloadpart++;
                                            Dispatcher.Invoke(() =>
                                            {
                                                // MessageBox.Show(downloadpart.ToString() + "\n-\n" + alldownloadpart);
                                                downloadp = (float)downloadpart / (float)alldownloadpart;
                                            });
                                            downloadbyte += fileBytes.LongLength;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        MessageBox.Show(e.Message + "\n" + weburi + "/" + spath);
                                        goto DownloadStart;
                                    }
                                }
                            }
                        }
                        
                        Dispatcher.Invoke(() =>
                        {
                            lab_download.Content = "解压\n资源";
                            downloadp = 0;
                        });

                        for (int i = 0; i < FILES.Count; i++)
                        {
                            var file = FILES[i];
                            if (file.isFinish) continue;
                            string spath = "download/" + md5(file.path) + ".7z." + GetStr(1);
                            string result = ExtractArchive("7z.exe", spath, "");
                            if (result != string.Empty)
                            {
                                MessageBox.Show(result);
                            }
                            downloadp += (float)i / FILES.Count;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            lab_download.Content = "应用\n签名";
                            downloadp = 1;
                        });

                        List<WebFile> newFILES = new List<WebFile>();
                        // 下载后的资源校验
                        for (int i = 0; i < FILES.Count; i++)
                        {
                            var file = FILES[i];
                            if (file.isFinish) continue;
                            var paths = file.path.Split('\\');
                            var hashpath = "";
                            for (int stri = 0; stri < paths.Length; stri++)
                            {
                                hashpath += md5(paths[stri]) + "\\";
                            }
                            hashpath = hashpath.Substring(0, hashpath.Length - 1);
                            if (!C_CheckFile(Encoding.Default.GetString(Encoding.Default.GetBytes(hashpath)), Encoding.Default.GetString(Encoding.Default.GetBytes(file.hash))))
                            {
                                // 下载后解压出来的资源错了，为什么？？？
                                // 这里理论上不可能运行到
                                // 除非用户自己一边下载一边改文件了！
                                // 这里发现了问题
                                if (Directory.Exists(file.path))
                                {
                                    Directory.Delete(file.path, true);
                                }
#if TEST
                                MessageBox.Show("下载文件校验失败？？？为什么？？？");
#endif

                                newFILES.Add(file);
                            }
                        }
                        
                        if (newFILES.Count != 0)
                        {
                            // 下载后解压出来的资源错了，为什么？？？
                            // 这里理论上不可能运行到
                            // 除非用户自己一边下载一边改文件了！
                            // 这里纠正问题
                            FILES = newFILES;
                            continue;
                        }
                        // 全部校验结束，从临时文件转为真实文件
                        for (int i = 0; i < FILES.Count; i++)
                        {
                            var file = FILES[i];
                            var paths = file.path.Split('\\');
                            var hashpath = "";
                            for (int stri = 0; stri < paths.Length; stri++)
                            {
                                hashpath += md5(paths[stri]) + "\\";
                            }
                            hashpath = hashpath.Substring(0, hashpath.Length - 1);
                            MoveAndRenameFile(hashpath, file.path);
                        }

                        if (Directory.Exists(midfilepath))
                        {
                            Directory.Delete(midfilepath, true);
                        }

                        if (Directory.Exists("download"))
                        {
                            Directory.Delete("download", true);
                        }

                        allokay = true;
                    }
                }
            });
            tUpdateCheck.Start();

            // 安装块
            var timerDownload = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.02),
            };
            timerDownload.Tick += (object? sender, EventArgs e) =>
            {
                downloadsindelay += 0.2f;
                ImageBrush brush = new ImageBrush();
                brush.ImageSource = CreateDynamicBitmapImage(256, 256);
                img_EnterGameA.OpacityMask = brush;
                img_EnterGameB.OpacityMask = brush;
            };
            timerDownload.Start();

            // 可以启动块
            //img_EnterGameA.RenderTransform = new RotateTransform() { Angle = 0, CenterX = 64, CenterY = 64 };
            //img_EnterGameB.RenderTransform = new RotateTransform() { Angle = 0, CenterX = 64, CenterY = 64 };
            //DispatcherTimer ta = new DispatcherTimer();
            //ta.Interval = TimeSpan.FromSeconds(0.02);
            //ta.Tick += (object? sender, EventArgs e) =>
            //{
            //    (img_EnterGameA.RenderTransform as RotateTransform).Angle += 0.5;
            //    (img_EnterGameB.RenderTransform as RotateTransform).Angle += 1.25;
            //};
            //ta.Start();
#endregion

        }

        private bool _isDragging = false;
        private Point _clickPosition;

        private void StartDrag(object sender, MouseEventArgs e)
        {
            // 鼠标左键按下时，开始拖动
            // MessageBox.Show(e.GetPosition(this).X + " " + e.GetPosition(this).Y);
            if (e.GetPosition(this).Y <= 24 + 24)
            { 
                _isDragging = true;
                _clickPosition = e.GetPosition(this);
            }
            
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            // 鼠标左键释放时，停止拖动
            _isDragging = false;
        }

        private void Draging(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // 计算新的窗口位置并更新窗口位置
                Point currentPosition = e.GetPosition(this);
                double offsetX = currentPosition.X - _clickPosition.X;
                double offsetY = currentPosition.Y - _clickPosition.Y;

                // 更新窗口位置
                this.Left += offsetX;
                this.Top += offsetY;
            }
        }

        private void StartButton_MouseEnter(object sender, MouseEventArgs e)
        {
            // MessageBox.Show("mouse enter");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (canentergame)
            {
                Process p = new Process();
                p.StartInfo.FileName = gamepath;
                p.Start();
            }
            else
            {
                if (lab_download.Content as string != "暂停\n下载" && lab_download.Content as string != "继续\n下载" && lab_download.Content as string != "获取\n游戏")
                {
                    return;
                }
                lock (canDownload)
                {
                    candownload = !candownload;
                    lab_download.Content = candownload ? "暂停\n下载" : "继续\n下载";
                }
            }
        }

        private void btn_close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #region 轮播

        private void btn_loop_Click(object sender, RoutedEventArgs e)
        {
            lock (looplock)
            {
                if (loopuri.ContainsKey(at))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = loopuri[at],
                        UseShellExecute = true // 确保使用默认浏览器打开链接
                    });
                }
            }
        }

        DispatcherTimer looptimer = new DispatcherTimer();

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
            //timer.Stop();
            //timer.Start();
            LoopNext();
        }

        private void btn_tureleft_Click(object sender, RoutedEventArgs e)
        {
            //timer.Stop();
            //timer.Start();
            LoopForward();
        }
        #endregion

        #region 咨讯
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
        #endregion

        #region 活动文本链接
        // 活动
        private void btn_huodong_Click(object sender, RoutedEventArgs e)
        {
            textnewsat = 0;
            WebSelect.Margin = new Thickness(54, 487, 824, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        // 公告
        private void btn_gonggao_Click(object sender, RoutedEventArgs e)
        {
            textnewsat = 1;
            WebSelect.Margin = new Thickness(114, 487, 764, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        // 咨讯
        private void btn_zixvn_Click(object sender, RoutedEventArgs e)
        {
            textnewsat = 2;
            WebSelect.Margin = new Thickness(174, 487, 704, 109);
            lab_info1.Content = newsTextDict[textnewsat].ElementAt(0).Key;
            lab_info2.Content = newsTextDict[textnewsat].ElementAt(1).Key;
            lab_info3.Content = newsTextDict[textnewsat].ElementAt(2).Key;
        }

        private void btn_info1_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(0).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }

        private void btn_info2_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(1).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }

        private void btn_info3_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = newsTextDict[textnewsat].ElementAt(2).Value,
                UseShellExecute = true // 确保使用默认浏览器打开链接
            });
        }
        #endregion

        
    }
}