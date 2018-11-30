using AccountDownload.Framework;
using AccountDownload.IDao;
using AccountDownload.Model;
using AccountDownload.Model.system;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AccountDownload.Dao
{
    public class WxAccountDao : NhibernateDao<WxAccount>, IWxAccountDao
    {
        #region 获取分页列表
        public IList<WxAccount> GetPageList(int page, int rows, string sort, string order, List<DataFilter> filters, string recordTime, out long total)
        {
            var strFilter = HqlstrByExtFilter.GetHqlstrByExtFilter(filters, "o");

            var query = _Session.CreateQuery(@"select o from WxAccount as o" +
                                              " where o.CreateTime<='" + recordTime + "'" +
                                              (string.IsNullOrEmpty(strFilter) ? string.Empty : " and " + strFilter) +
                                              " order by o." + sort + " " + order)
                                              .SetFirstResult(page)
                                              .SetMaxResults(rows);

            total = _Session.CreateQuery(@"select count(o) from WxAccount as o" +
                                          " where o.CreateTime<='" + recordTime + "'" +
                                          (string.IsNullOrEmpty(strFilter) ? string.Empty : " and " + strFilter))
                                          .UniqueResult<long>();

            return query.List<WxAccount>();
        }
        #endregion

        #region 获取数据总数
        public long GetListCount(List<DataFilter> filters)
        {
            var strFilter = HqlstrByExtFilter.GetHqlstrByExtFilter(filters, "o");

            var query = _Session.CreateQuery(@"select count(o) from WxAccount as o" +
                                              (string.IsNullOrEmpty(strFilter) ? string.Empty : " where " + strFilter))
                                              .UniqueResult<long>();

            return query;
        }
        #endregion

        #region 校验是否需要保存微信账单
        public bool CheckIsSave(List<DataFilter> filters, int maxCount)
        {
            //true:需要保存 false:无需保存
            bool isSave = true;
            //获取已保存数据总数
            long total = GetListCount(filters);

            //如果历史数据数量 与 所需读取数据数不符，删除历史数据，重新保存
            //可能出现下载中人为中断导致数据未完全下载，所以需要此判断，防止数据量不一致
            if (maxCount == 0 || total != maxCount)
            {
                //需要保存数据
                isSave = true;
            }
            else
            {
                //无需保存数据
                isSave = false;
            }

            return isSave;
        }
        #endregion

        #region 删除数据
        public void Delete(List<DataFilter> filters)
        {
            int page = 0;
            int rows = 1000;
            long total = 0;
            int start = 0;
            string recordTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<WxAccount> list = new List<WxAccount>();
            IList<WxAccount> tempList = null;

            //每次获取1000条，防崩溃
            do
            {
                start = page * rows;
                tempList = GetPageList(start, rows, "CreateTime", "asc", filters, recordTime, out total);
                list.AddRange(tempList);
                page++;
            } while (tempList.Count() > 0);

            //删除
            foreach (var item in list)
            {
                Stateless_Delete(item);
            }
        }
        #endregion
    }
}
