using AccountDownload.IDao;
using AccountDownload.Model;
using AccountDownload.Model.system;
using AccountDownload.Unity;
using Aop.Api;
using Aop.Api.Request;
using Aop.Api.Response;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using WxPayAPI;

namespace AccountDownload
{
    public partial class AccountDownloadForm : Form
    {
        IWxAccountDao wxAccountDao = UnityHelper.UnityToT<IWxAccountDao>();
        ISysGlobalDao sysGlobalDao = UnityHelper.UnityToT<ISysGlobalDao>();
        IErrorLogDao errorLogDao = UnityHelper.UnityToT<IErrorLogDao>();
        IStoreDao storeDao = UnityHelper.UnityToT<IStoreDao>();
        IZFBAccountDao ZFBAccountDao = UnityHelper.UnityToT<IZFBAccountDao>();
        //是否正在下载中
        private bool isDownload = false;
        //微信账单自动更新日期
        private DateTime wxUpdateDate = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 09:00:00"));
        //支付宝账单自动更新日期
        private DateTime zfbUpdateDate = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 09:00:00"));

        public AccountDownloadForm()
        {
            InitializeComponent();
        }

        private void AccountDownloadForm_Load(object sender, EventArgs e)
        {

        }

        #region 系统托盘鼠标点击事件
        private void notifyIcon_AutoDownloadAccount_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.WindowState = FormWindowState.Normal;
            }
        }
        #endregion

        #region 点击菜单退出
        private void MenuItem_Exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
        #endregion

        //******************* 微信 *******************

        #region 下载微信账单按钮点击事件
        private void btn_DownloadWxAccount_Click(object sender, EventArgs e)
        {
            if (isDownload)
            {
                MessageBox.Show("程序正在自动下载账单中，请稍后再试");
                return;
            }

            //下载中
            isDownload = true;
            string beginDateStr = txt_WxBeginDate.Text.Trim();//起始日期字符串
            string endDateStr = txt_WxEndDate.Text.Trim();//结束日期字符串
            DateTime now = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd"));//当前日期
            DateTime maxDate = now.AddMonths(-3);//起始日期上限
            DateTime beginDate;//起始日期
            DateTime endDate;//结束日期

            #region 校验日期
            //校验录入日期
            if (string.IsNullOrEmpty(beginDateStr) || string.IsNullOrEmpty(endDateStr))
            {
                MessageBox.Show("请输入起始日期与结束日期");
                //下载完毕
                isDownload = false;
                return;
            }
            try
            {
                beginDate = Convert.ToDateTime(beginDateStr);
                endDate = Convert.ToDateTime(endDateStr);
            }
            catch (Exception)
            {
                MessageBox.Show("请输入正确的日期格式");
                //下载完毕
                isDownload = false;
                return;
            }

            //校验起始日期
            if (beginDate < maxDate)
            {
                MessageBox.Show("起始日期为3个月内");
                //下载完毕
                isDownload = false;
                return;
            }
            //校验结束日期
            if (endDate >= now)
            {
                MessageBox.Show("结束日期不能超出或等于今天");
                //下载完毕
                isDownload = false;
                return;
            }

            //校验起始日期与结束日期
            if (beginDate > endDate)
            {
                MessageBox.Show("起始日期不能超过结束日期");
                //下载完毕
                isDownload = false;
                return;
            }
            #endregion

            //禁用按钮
            btn_WxDownload.Text = "下载中..";
            btn_WxDownload.Enabled = false;
            //下载微信账单
            DownloadWxAccount(beginDate, endDate, "ALL");
            //下载完成提示
            MessageBox.Show("下载完毕");
            //下载完毕
            isDownload = false;
            //启用按钮
            btn_WxDownload.Text = "手动下载";
            btn_WxDownload.Enabled = true;
        }
        #endregion

        #region 时间控件(自动下载微信账单)
        private void timer_AutoDownloadWxAccount_Tick(object sender, EventArgs e)
        {
            //当前时间 大于等于 更新日期 并且 可下载状态
            if (DateTime.Now >= wxUpdateDate && isDownload == false)
            {
                //下载中
                isDownload = true;
                //禁用下载按钮
                btn_WxDownload.Text = "自动下载中";
                btn_WxDownload.Enabled = false;
                //需下载的账单日期
                DateTime beginDate = DateTime.Now.AddDays(-1);
                //下载微信账单
                DownloadWxAccount(beginDate, beginDate, "ALL");
                //显示更新时间提示
                lab_WxDownloadTips.Text = "上次更新时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                //保存最近一次更新时间
                SysGlobal sysGlobal = sysGlobalDao.GetAll().FirstOrDefault();
                sysGlobal.WxAccountUpdateTime = DateTime.Now;
                sysGlobal.UpdateTime = DateTime.Now;
                sysGlobalDao.SaveOrUpdate(sysGlobal);

                //更新时间变更为明天
                wxUpdateDate = wxUpdateDate.AddDays(1);
                //下载完毕
                isDownload = false;
                //启用下载按钮
                btn_WxDownload.Text = "手动下载";
                btn_WxDownload.Enabled = true;
            }
        }
        #endregion

        #region 下载微信账单
        private void DownloadWxAccount(DateTime beginDate, DateTime endDate, string type)
        {
            do
            {
                string date = beginDate.ToString("yyyyMMdd");

                //date格式：20180808
                //type格式：
                //【ALL 所有订单信息
                //SUCCESS 成功支付的订单
                //REFUND 退款订单
                //REVOKED 撤销的订单】
                string result = DownloadBill.Run(date, type);
                int currentIndex = 0;//当前索引
                string line = "";//每行内容
                int count = 0; //总行数
                int maxCount = 0;//所需读取数据数
                //读取流
                StringReader reader = null;

                #region 计算读取数
                reader = new StringReader(result);

                while (line != null)
                {
                    line = reader.ReadLine();
                    count++;
                }

                //所需读取数据数
                maxCount = count - 4;
                //重置
                line = "";
                //关闭流
                reader.Close();
                #endregion

                #region 校验是否需要保存数据
     
                #region 过滤条件
                List<DataFilter> filters = new List<DataFilter>();
                //大于等于起始日期
                filters.Add(new DataFilter { comparison = "gteq", field = "PayTime", type = "date", value = beginDate.ToString("yyyy-MM-dd 00:00:00") });
                //小于等于起始日期
                filters.Add(new DataFilter { comparison = "lteq", field = "PayTime", type = "date", value = beginDate.ToString("yyyy-MM-dd 23:59:59") });
                #endregion

                //校验是否需要保存微信账单
                bool isSave = wxAccountDao.CheckIsSave(filters, maxCount);

                if (isSave)
                {
                    //需要保存
                    //删除历史数据
                    wxAccountDao.Delete(filters);
                }
                else
                {
                    //不需要保存
                    //增加天数
                    beginDate = beginDate.AddDays(1);
                    continue;
                }

                #endregion

                #region 保存数据
                reader = new StringReader(result);

                //不读取超出的数据
                while (line != null && currentIndex <= maxCount)
                {
                    line = reader.ReadLine();

                    #region 读取数据行
                    if (currentIndex > 0)
                    {
                        string[] accounts = line.Split(',');
                        //店铺名称
                        string storeName = accounts[20].Trim().Split(' ')[0];
                        //支付方式
                        string payModel = "";
                        //门店名称分割出支付方式时，因提供的格式不对有可能分割出现报错
                        try
                        {
                            payModel = accounts[20].Trim().Split(' ')[1];
                        }
                        catch (Exception) { }
                        //费率
                        decimal rate = Convert.ToDecimal(accounts[23].Trim().Split('%')[0]);

                        WxAccount entity = new WxAccount
                        {
                            PayTime = Convert.ToDateTime(accounts[0].Trim()),//交易时间
                            AppId = accounts[1].Trim(),//AppID
                            MchId = accounts[2].Trim(),//商户号
                            SpecialMchId = accounts[3].Trim(),//特约商户号
                            DeviceId = accounts[4].Trim(),//设备号
                            OrderNo = accounts[5].Trim(),//微信订单号
                            MchOrderNo = accounts[6].Trim(),//商户订单号
                            UserOpenId = accounts[7].Trim(),//用户OpenId
                            PayType = accounts[8].Trim(),//交易类型
                            PayState = accounts[9].Trim(),//交易状态
                            PayBank = accounts[10].Trim(),//付款银行
                            MoneyType = accounts[11].Trim(),//货币种类
                            DiscountsTotal = Convert.ToDecimal(accounts[12].Trim()),//应结订单金额
                            CouponPrice = Convert.ToDecimal(accounts[13].Trim()),//代金券金额
                            RefundOrderNo = accounts[14].Trim(),//微信退款单号
                            MchRefundOrderNo = accounts[15].Trim(),//商户退款单号
                            RefundPrice = Convert.ToDecimal(accounts[16].Trim()),//退款金额
                            CouponRefundPrice = Convert.ToDecimal(accounts[17].Trim()),//充值券退款金额
                            RefundType = accounts[18].Trim(),//退款类型
                            RefundState = accounts[19].Trim(),//退款状态
                            StoreName = storeName,//店铺名称
                            PayModel = payModel,//支付方式
                            DataPackage = accounts[21].Trim(),//商户数据包
                            ServicePrice = Convert.ToDecimal(accounts[22].Trim()),//手续费
                            Rate = rate,//费率
                            Total = Convert.ToDecimal(accounts[24].Trim()),//订单金额
                            SendRefundPrice = Convert.ToDecimal(accounts[25].Trim()),//申请退款金额
                            RateRemark = accounts[26].Trim()//费率备注
                        };
                        wxAccountDao.Stateless_Insert(entity);
                    }
                    #endregion

                    currentIndex++;
                }

                //关闭流
                reader.Close();
                #endregion

                //增加天数
                beginDate = beginDate.AddDays(1);
            } while (beginDate <= endDate);
        }
        #endregion

        //******************* 支付宝 *******************

        #region 获取密钥路径
        public static string GetCurrentPath()
        {
            string basePath = Environment.CurrentDirectory.Replace("\\bin\\Debug", "") + "\\Keys\\";
            return basePath;
        }
        #endregion

        #region 获取支付宝接口配置
        public static IAopClient GetAopClient()
        {
            //// -----沙箱地址-----
            //string serverUrl = "http://openapi.alipaydev.com/gateway.do";
            // -----线上地址-----
            string serverUrl = "https://openapi.alipay.com/gateway.do";
            //应用ID
            string appId = "2018091161387105";
            //商户公钥
            string publicKeyPem = GetCurrentPath() + "已来生活授权公钥.pem";
            //商户私钥
            string privateKeyPem = GetCurrentPath() + "已来生活授权私钥.pem";
            //商户支付宝公钥
            string publicZFBKeyPem = GetCurrentPath() + "已来生活授权支付宝公钥.pem";

            return new DefaultAopClient(serverUrl, appId, privateKeyPem, "json", "1.0", "RSA2", publicZFBKeyPem, "utf-8", true);
        }
        #endregion

        #region 下载支付宝账单按钮点击事件
        private void btn_ZFBDownload_Click(object sender, EventArgs e)
        {
            if (isDownload)
            {
                MessageBox.Show("程序正在自动下载账单中，请稍后再试");
                return;
            }

            //下载中
            isDownload = true;
            string beginDateStr = txt_ZFBBeginDate.Text.Trim();//起始日期字符串
            string endDateStr = txt_ZFBEndDate.Text.Trim();//结束日期字符串
            DateTime now = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd"));//当前日期
            DateTime maxDate = now.AddMonths(-3);//起始日期上限
            DateTime beginDate;//起始日期
            DateTime endDate;//结束日期

            #region 校验日期
            //校验录入日期
            if (string.IsNullOrEmpty(beginDateStr) || string.IsNullOrEmpty(endDateStr))
            {
                MessageBox.Show("请输入起始日期与结束日期");
                //下载完毕
                isDownload = false;
                return;
            }
            try
            {
                beginDate = Convert.ToDateTime(beginDateStr);
                endDate = Convert.ToDateTime(endDateStr);
            }
            catch (Exception)
            {
                MessageBox.Show("请输入正确的日期格式");
                //下载完毕
                isDownload = false;
                return;
            }

            //校验起始日期
            if (beginDate < maxDate)
            {
                MessageBox.Show("起始日期为3个月内");
                //下载完毕
                isDownload = false;
                return;
            }
            //校验结束日期
            if (endDate >= now)
            {
                MessageBox.Show("结束日期不能超出或等于今天");
                //下载完毕
                isDownload = false;
                return;
            }

            //校验起始日期与结束日期
            if (beginDate > endDate)
            {
                MessageBox.Show("起始日期不能超过结束日期");
                //下载完毕
                isDownload = false;
                return;
            }
            #endregion

            //禁用按钮
            btn_ZFBDownload.Text = "下载中..";
            btn_ZFBDownload.Enabled = false;
            //下载支付宝账单 bill_type格式:trade指商户基于支付宝交易收单的业务账单；signcustomer是指基于商户支付宝余额收入及支出等资金变动的帐务账单
            DownloadZFBAccount(beginDate, endDate, "trade");
            //下载完成提示
            MessageBox.Show("下载完毕");
            //下载完毕
            isDownload = false;
            //启用按钮
            btn_ZFBDownload.Text = "手动下载";
            btn_ZFBDownload.Enabled = true;
        }
        #endregion

        #region 时间控件(自动下载支付宝账单)
        private void timer_AutoDownloadZFBAccount_Tick(object sender, EventArgs e)
        {
            //当前时间 大于等于 更新日期 并且 可下载状态
            if (DateTime.Now >= zfbUpdateDate && isDownload == false)
            {
                //下载中
                isDownload = true;
                //禁用下载按钮
                btn_ZFBDownload.Text = "自动下载中";
                btn_ZFBDownload.Enabled = false;
                //需下载的账单日期
                DateTime beginDate = DateTime.Now.AddDays(-1);
                //下载支付宝账单 bill_type格式:trade指商户基于支付宝交易收单的业务账单；signcustomer是指基于商户支付宝余额收入及支出等资金变动的帐务账单
                DownloadZFBAccount(beginDate, beginDate, "trade");
                //显示更新时间提示
                lab_ZFBDownloadTips.Text = "上次更新时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                //保存最近一次更新时间
                SysGlobal sysGlobal = sysGlobalDao.GetAll().FirstOrDefault();
                sysGlobal.ZFBAccountUpdateTime = DateTime.Now;
                sysGlobal.UpdateTime = DateTime.Now;
                sysGlobalDao.SaveOrUpdate(sysGlobal);

                //更新时间变更为明天
                zfbUpdateDate = zfbUpdateDate.AddDays(1);
                //下载完毕
                isDownload = false;
                //启用下载按钮
                btn_ZFBDownload.Text = "手动下载";
                btn_ZFBDownload.Enabled = true;
            }
        }
        #endregion

        #region 下载支付宝账单
        private void DownloadZFBAccount(DateTime beginDate, DateTime endDate, string bill_type)
        {
            IAopClient client = GetAopClient();
            AlipayDataDataserviceBillDownloadurlQueryRequest request = new AlipayDataDataserviceBillDownloadurlQueryRequest();
            string root = Environment.CurrentDirectory.Replace("\\bin\\Debug", "") + "\\Download";

            //校验Download文件夹是否存在
            if (!Directory.Exists(root))
            {
                //不存在 创建Download文件夹
                Directory.CreateDirectory(root);
            }

            //压缩包路径
            string zipPath = root + "\\支付宝账单.zip";
            //文件路径
            string filePath = "";

            //遍历天数
            do
            {
                #region 账单参数
                //bill_type格式:trade指商户基于支付宝交易收单的业务账单；signcustomer是指基于商户支付宝余额收入及支出等资金变动的帐务账单
                //bill_date格式：2018-09-13
                string bill_date = beginDate.ToString("yyyy-MM-dd");

                request.BizContent = "{" +
                    "\"bill_type\":\"" + bill_type + "\"," +
                    "\"bill_date\":\"" + bill_date + "\"" +
                "}";
                #endregion

                //获取已授权商户集合
                IList<Store> storeList = storeDao.GetOauthList().Where(o => string.IsNullOrEmpty(o.ZFBMch_Id) == false).ToList();

                //遍历商户
                foreach (var store in storeList)
                {
                    //appAuthToken：某个商户的appAuthToken(201809BBb2b88d5c11f348b2a92be0c394b05A11)
                    string appAuthToken = store.app_auth_token;
                    AlipayDataDataserviceBillDownloadurlQueryResponse response = client.Execute(request, "", appAuthToken);

                    if (response.Msg == "Success")
                    {
                        #region 下载账单
                        try
                        {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(response.BillDownloadUrl);
                            req.ServicePoint.Expect100Continue = false;
                            req.Method = "GET";
                            req.KeepAlive = true;
                            //req.ContentType = "zip";// "image/png";
                            using (HttpWebResponse rsp = (HttpWebResponse)req.GetResponse())
                            {
                                using (Stream reader = rsp.GetResponseStream())
                                {
                                    using (FileStream writer = new FileStream(zipPath, FileMode.OpenOrCreate, FileAccess.Write))
                                    {
                                        byte[] buff = new byte[512];
                                        int c = 0; //实际读取的字节数
                                        while ((c = reader.Read(buff, 0, buff.Length)) > 0)
                                        {
                                            writer.Write(buff, 0, c);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errorLogDao.SaveErrorLog("支付宝对账单下载出错：" + ex.Message);
                        }
                        #endregion

                        #region 解压账单
                        ZipInputStream zipStream = new ZipInputStream(File.Open(zipPath, FileMode.Open));
                        ZipEntry entry = zipStream.GetNextEntry();

                        while (entry != null)
                        {
                            if (!entry.IsFile)
                            {
                                continue;
                            }

                            //不解压汇总压缩包
                            if (entry.Name.Contains("汇总"))
                            {
                                //读取下一个压缩包
                                entry = zipStream.GetNextEntry();
                                continue;
                            }

                            //20885225452401150156_20180912_业务明细.csv
                            //20885225452401150156_20180912_业务明细(汇总).csv
                            filePath = Environment.CurrentDirectory.Replace("\\bin\\Debug", "") + "\\Download\\" + entry.Name;
                            //解压后的文件
                            FileStream writer = File.Create(filePath);

                            int bufferSize = 2048; //缓冲区大小
                            int readCount = 0; //读入缓冲区的实际字节
                            byte[] buffer = new byte[bufferSize];
                            readCount = zipStream.Read(buffer, 0, bufferSize);
                            while (readCount > 0)
                            {
                                writer.Write(buffer, 0, readCount);
                                readCount = zipStream.Read(buffer, 0, bufferSize);
                            }

                            writer.Close();
                            writer.Dispose();

                            //读取下一个压缩包
                            entry = zipStream.GetNextEntry();
                        }

                        zipStream.Close();
                        zipStream.Dispose();
                        #endregion

                        #region 遍历账单
                        StreamReader sr = new StreamReader(filePath, Encoding.GetEncoding("gb2312"));
                        string line = "";//每行内容
                        int lineIndex = 0;//第一行为标题
                        int count = 0;//总行数
                        int maxCount = 0;//所需读取数据数
                        int maxIndex = 0;//所属读取索引数
                        ZFBAccount entity = null;

                        #region 计算读取数
                        while (line != null)
                        {
                            line = sr.ReadLine();
                            count++;
                        }

                        //所需读取数据数
                        maxCount = count - 10;
                        //所属读取索引数
                        maxIndex = count - 5;
                        //重置
                        line = "";
                        //关闭流
                        sr.Close();
                        #endregion

                        #region 校验是否需要保存数据

                        #region 过滤条件
                        List<DataFilter> filters = new List<DataFilter>();
                        //匹配商户号
                        filters.Add(new DataFilter { comparison = "eq", field = "MchId", type = "string", value = store.ZFBMch_Id });
                        //大于等于起始日期
                        filters.Add(new DataFilter { comparison = "gteq", field = "PayTime", type = "date", value = beginDate.ToString("yyyy-MM-dd 00:00:00") });
                        //小于等于起始日期
                        filters.Add(new DataFilter { comparison = "lteq", field = "PayTime", type = "date", value = beginDate.ToString("yyyy-MM-dd 23:59:59") });
                        #endregion

                        //校验是否需要保存支付宝账单
                        bool isSave = ZFBAccountDao.CheckIsSave(filters, maxCount);

                        if (isSave)
                        {
                            //需要保存
                            //删除历史数据
                            ZFBAccountDao.Delete(filters);
                        }
                        else
                        {
                            //不需要保存
                            //换下一个商家
                            continue;
                        }

                        #endregion

                        sr = new StreamReader(filePath, Encoding.GetEncoding("gb2312"));
                        //商户号
                        string mchId = "";

                        //支付宝交易号,商户订单号,业务类型,商品名称,创建时间,完成时间,门店编号,门店名称,操作员,终端号,对方账户,订单金额（元）,商家实收（元）,支付宝红包（元）,集分宝（元）,支付宝优惠（元）,商家优惠（元）,券核销金额（元）,券名称,商家红包消费金额（元）,卡消费金额（元）,退款批次号/请求号,服务费（元）,分润（元）,备注
                        while (line != null && lineIndex < maxIndex)
                        {
                            line = sr.ReadLine();

                            #region 读取商户号行
                            if (lineIndex == 1)
                            {
                                //#账号：[20885225452401150156] 处理后得到 2088522545240115
                                mchId = line.Split('[')[1].Substring(0, 16);
                            }
                            #endregion

                            #region 读取数据行，保存账单
                            if (lineIndex > 4 && line != null)
                            {
                                string[] accounts = line.Split(',');

                                //创建新账单
                                entity = new ZFBAccount
                                {
                                    MchId = mchId,//商户号
                                    OrderNo = accounts[0].Trim(),//支付宝交易号
                                    MchOrderNo = accounts[1].Trim(),//商户订单号
                                    PayType = accounts[2].Trim(),//业务类型
                                    GoodsName = accounts[3].Trim(),//商品名称
                                    CreateTime = Convert.ToDateTime(accounts[4].Trim()),//创建时间
                                    PayTime = Convert.ToDateTime(accounts[5].Trim()),//完成时间
                                    StoreNo = accounts[6].Trim(),//门店编号
                                    StoreName = accounts[7].Trim(),//门店名称
                                    Handler = accounts[8].Trim(),//操作员
                                    DeviceId = accounts[9].Trim(),//终端号
                                    PayUser = accounts[10].Trim(),//对方账户
                                    Total = Convert.ToDecimal(accounts[11].Trim()),//订单金额
                                    DiscountsTotal = Convert.ToDecimal(accounts[12].Trim()),//商家实收（元）
                                    ZFB_RedPacket = Convert.ToDecimal(accounts[13].Trim()),//支付宝红包（元）
                                    SetPoints = Convert.ToDecimal(accounts[14].Trim()),//集分宝（元）
                                    ZFB_Discounts = Convert.ToDecimal(accounts[15].Trim()),//支付宝优惠（元）
                                    Store_Discounts = Convert.ToDecimal(accounts[16].Trim()),//商家优惠（元）
                                    CouponPrice = Convert.ToDecimal(accounts[17].Trim()),//券核销金额（元）
                                    CouponName = accounts[18].Trim(),//券名称
                                    Store_RedPacket = Convert.ToDecimal(accounts[19].Trim()),//商家红包消费金额（元）
                                    CardPrice = Convert.ToDecimal(accounts[20].Trim()),//卡消费金额（元）
                                    RefundOrderNo = accounts[21].Trim(),//退款批次号/请求号
                                    ServicePrice = Convert.ToDecimal(accounts[22].Trim()),//服务费（元）
                                    SharePrice = Convert.ToDecimal(accounts[23].Trim()),//分润（元）
                                    Remark = accounts[24].Trim()//备注
                                };

                                //保存账单
                                ZFBAccountDao.Stateless_Insert(entity);
                            }
                            #endregion

                            lineIndex++;
                        }
                        //关闭流
                        sr.Close();
                        #endregion

                        #region 删除文件
                        //删除文件
                        File.Delete(filePath);

                        //删除压缩包
                        File.Delete(zipPath);
                        #endregion
                    }
                }

                //增加天数
                beginDate = beginDate.AddDays(1);
            } while (beginDate <= endDate);
        }
        #endregion
    }
}
