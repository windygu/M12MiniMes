﻿using CommunicateCenter;
using Faster.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using M12MiniMes.Entity;
using WHC.Framework.ControlUtil;
using M12MiniMes.BLL;

namespace M12MiniMes.UIStart
{
    public static class TcpServer
    {
        public static AsyncTcpServer Server;

        public static bool Save() 
        {
            CommonSerializer.SaveObjAsBinaryFile(Server, $@"D:\FastAutomation\Server.xml", out bool bSaveOK, out Exception ex);
            return bSaveOK;
        }

        public static bool Load() 
        {
            Server = CommonSerializer.LoadObjFormBinaryFile<AsyncTcpServer>($@"D:\FastAutomation\Server.xml", out bool bLoadOK, out Exception ex);
            return bLoadOK;
        }

        //主逻辑处理
        public static void StartMasterLogic() 
        {
            Server.DataReceived += Server_DataReceived;

            //开一个线程 给所有客户端发送心跳 10S一次
            //Task.Factory.StartNew(async() =>
            //{
            //    while (true)
            //    {
            //        try
            //        {
            //            byte[] bt = Encoding.UTF8.GetBytes($@"{Header.XT.ToString()}");
            //            Server.SendMesAsyncToAllClients(bt);
            //            await Task.Delay(10000);
            //        }
            //        catch (Exception ex)
            //        {
            //            LogService.Warn(ex.Message, ex);
            //        }
            //    }
            //});
        }

        private static void Server_DataReceived(FastInterface.ITcpServer listener, Socket client, byte[] data, int length)
        {
            try
            {
                string mes = Encoding.UTF8.GetString(data, 0, length);  //客户端发送过来的数据
                if (string.IsNullOrEmpty(mes))
                {
                    return;
                }
                string[] mess = mes.Split(',');
                if (mess.Count() <= 1)  //无效信息，不在定义的通讯协议之内
                {
                    var var = Encoding.UTF8.GetBytes("invalid data!");
                    listener.SendMesAsyncToClient(client, var);
                    return;
                }
                string strHeader = mess[0]; //行头
                if (!Enum.GetNames(typeof(Header)).Contains(strHeader)) //不在定义的行头
                {
                    //未定义的通讯格式！;
                    var var = Encoding.UTF8.GetBytes("Undefined Header!");
                    listener.SendMesAsyncToClient(client, var);
                    return;
                }

                IPAddress ip = ((IPEndPoint)client.RemoteEndPoint).Address;
                string strIP = ip.ToString();
                //找到该地址对应的设备信息
                MachineItem machineItem = ItemManager.Instance.GetMachineItemByIP(strIP);
                if (machineItem == null)
                {
                    throw new Exception($@"查询设备信息无该记录IP {strIP} ，请先进行同步！");
                }

                Header header = (Header)Enum.Parse(typeof(Header), strHeader);
                string[] parameters = mess.Skip(1).ToArray();
                string strInMachineID = ""; //当前流入设备ID
                string strInMachineName = ""; //当前流入设备名称
                MachineItem InMachineItem = null; //当前流入设备Item
                string strCMachineID = ""; //被查询设备ID
                string strCMachineName = ""; //被查询设备名称
                MachineItem CMachineItem = null; //被查询设备Item
                string rfid = "";
                FixtureItem fixtureItem = null; //当前治具
                byte[] dataSend = null;

                switch (header)
                {
                    case Header.CX:  //查询生产数据
                        strInMachineID = parameters[0];
                        InMachineItem = ItemManager.Instance.GetMachineItemByID(strInMachineID);
                        strInMachineName = InMachineItem.设备名称;
                        strCMachineID = parameters[1];
                        rfid = parameters[2];
                        fixtureItem = ItemManager.Instance.GetFixtureItem(rfid, strInMachineID);
                        if (fixtureItem == null) //找不到该RFID治具的内存信息  不允许新治具跳过线头设备流入生产线（即要求新治具必须从线头开始流入）
                        {
                            var var = Encoding.UTF8.GetBytes($@"get the fixture failed which rfid is {rfid} !");
                            listener.SendMesAsyncToClient(client, var);
                            return;
                        }
                        string strData = $@"CX,{strCMachineID},{rfid},{fixtureItem.治具生产批次号}";
                        if (fixtureItem.MaterialItems.Count == 0) //如果第一次治具不携带任何物料，则赋予12个null进去
                        {
                            for (int i = 0; i < 12; i++)
                            {
                                fixtureItem.MaterialItems.Add(null);
                            }
                        }
                        #region 特殊设备需要读写两个参数
                        string[] specialMachine = new string[6] { "1", "2", "3", "6", "10", "11" };
                        string itemEmptyMes = "";
                        if (specialMachine.Contains(strInMachineID))
                        {
                            itemEmptyMes = @",0/0";
                        }
                        else
                        {
                            itemEmptyMes = ",0";
                        }
                        #endregion
                        foreach (var item in fixtureItem.MaterialItems)
                        {
                            if (item == null)
                            {
                                strData += itemEmptyMes;
                                continue;
                            }
                            var var = item.生产数据.Where(p => p.设备id.ToString() == strCMachineID);
                            if (var == null)
                            {
                                strData += itemEmptyMes;
                                continue;
                            }
                            //找出物料指定设备ID的工序数据
                            var var2 = var.Select(p => p.工序数据).FirstOrDefault() ?? itemEmptyMes;
                            if (!var2.StartsWith(","))
                            {
                                var2 = $@",{var2}";
                            }
                            strData += var2;
                        }
                        dataSend = Encoding.UTF8.GetBytes(strData);
                        listener.SendMesAsyncToClient(client, dataSend);
                        break;
                    case Header.XR: //写入生产数据
                        rfid = parameters[0];
                        strInMachineID = parameters[1];
                        InMachineItem = ItemManager.Instance.GetMachineItemByID(strInMachineID);
                        fixtureItem = ItemManager.Instance.GetFixtureItem(rfid, strInMachineID);
                        if (fixtureItem == null) //找不到该RFID治具的内存信息
                        {
                            var var = Encoding.UTF8.GetBytes($@"get the fixture failed which rfid is {rfid} !");
                            listener.SendMesAsyncToClient(client, var);
                            return;
                        }
                        int index = 2; //parameters解析索引
                        int numbers = 12; //一般是写入12个物料信息
                        int k = parameters.Skip(2).Count() / 2;
                        numbers = Math.Min(k, 12);
                        for (int i = 0; i < numbers; i++)   //写入数据格式：值1,是否ok1，值2,是否ok2，……值12,是否ok12
                        {
                            MaterialItem materialItem = fixtureItem.MaterialItems[i];
                            if (materialItem == null)
                            {
                                materialItem = new MaterialItem(fixtureItem);
                                fixtureItem.InsertMaterialItem(i, materialItem);
                            }

                            string 物料guid = materialItem.MaterialGuid.ToString();
                            string 治具guid = materialItem.Fixture.FixtureGuid.ToString();
                            int 设备id = int.Parse(strInMachineID);

                            //检测是第一次写入还是再次写入刷新生产数据 最好规定下位机只允许写入一次
                            生产数据表Info scData = materialItem.生产数据.FirstOrDefault(p =>
                                p.物料guid.Equals(物料guid)
                                && p.治具guid.Equals(治具guid)
                                && p.设备id.Equals(设备id)
                                );

                            bool firstWrite = false;  //是否第一次写入
                            if (scData == null)  //是第一次写入
                            {
                                scData = new 生产数据表Info();
                                materialItem.生产数据.Add(scData);
                                firstWrite = true;
                            }
                            scData.生产时间 = DateTime.Now;
                            scData.物料生产批次号 = materialItem.物料生产批次号;
                            scData.治具生产批次号 = materialItem.Fixture.治具生产批次号;
                            scData.物料guid = 物料guid;
                            scData.治具guid = 治具guid;
                            scData.治具rfid = rfid;
                            scData.治具孔号 = materialItem.GetHoleIndexInFixture();
                            scData.设备id = 设备id;
                            scData.设备名称 = strInMachineName;
                            scData.工位号 = "";
                            scData.工序数据 = parameters[index];
                            scData.结果ok = parameters[index + 1] == "1";  //0表示无 1表示OK 2表示NG
                            if (firstWrite)
                            {
                                scData.生产数据id = BLLFactory<生产数据表>.Instance.Insert2(scData);  //写入一条数据到数据库中
                                #region 如果是线头机，该批次的上线数+12
                                if (strInMachineID == "0" && i == 11)
                                {
                                    string condition = $@"生产批次号 = '{materialItem.Fixture.治具生产批次号}'";
                                    var var = BLLFactory<生产批次生成表>.Instance.FindLast(condition);
                                    if (var != null)
                                    {
                                        var.上线数 += 12;
                                        if (var.上线数 >= var.计划投入数)
                                        {
                                            var.状态 = "生产完成";
                                        }
                                        BLLFactory<生产批次生成表>.Instance.Update(var, var.生产批次id);
                                    }
                                }
                                #endregion
                            }
                            else
                            {
                                BLLFactory<生产数据表>.Instance.Update(scData, scData.生产数据id);  //更新一条数据到数据库中
                            }
                            index += 2;
                        }
                        dataSend = Encoding.UTF8.GetBytes("XROK"); //返回下位机"写入完成"
                        listener.SendMesAsyncToClient(client, dataSend);
                        break;
                    case Header.NGTH:  //NG替换
                                       //1、把一个NG物料从治具上取出并丢弃后，腾出位置
                                       //2、从暂存位的治具上取一个好物料出来，放到上述腾出位置
                        string preRFID = parameters[0];
                        string strPreHoleIndex = parameters[1];
                        int iPreHoleIndex = int.Parse(strPreHoleIndex);
                        string nowRFID = parameters[2];
                        string strNowHoleIndex = parameters[3];
                        int iNowHoleIndex = int.Parse(strNowHoleIndex);
                        strInMachineID = parameters[4];
                        string stationID = parameters[5];

                        InMachineItem = ItemManager.Instance.GetMachineItemByID(strInMachineID);
                        strInMachineName = InMachineItem.设备名称;
                        FixtureItem preFixture = ItemManager.Instance.GetFixtureItem(preRFID, strInMachineID); //替换前治具
                        FixtureItem nowFixture = ItemManager.Instance.GetFixtureItem(nowRFID, strInMachineID); //替换后治具
                        MaterialItem thMaterialItem = preFixture.MaterialItems.ElementAtOrDefault(iPreHoleIndex); //替换的物料
                        MaterialItem ngMaterialItem = nowFixture.MaterialItems.ElementAtOrDefault(iNowHoleIndex);

                        物料ng替换记录表Info ngInfo = new 物料ng替换记录表Info();
                        ngInfo.Ng替换时间 = DateTime.Now;
                        ngInfo.物料生产批次号 = ngMaterialItem?.物料生产批次号;
                        ngInfo.设备id = int.Parse(strInMachineID);
                        ngInfo.设备名称 = strInMachineName;
                        ngInfo.工位号 = stationID;
                        ngInfo.物料guid = ngMaterialItem?.MaterialGuid.ToString();
                        ngInfo.替换前治具guid = preFixture.FixtureGuid.ToString();
                        ngInfo.替换前治具rfid = preRFID;
                        ngInfo.替换前治具孔号 = iPreHoleIndex;
                        ngInfo.前治具生产批次号 = preFixture.治具生产批次号;
                        ngInfo.替换后治具guid = nowFixture.FixtureGuid.ToString();
                        ngInfo.替换后治具rfid = nowRFID;
                        ngInfo.替换后治具孔号 = iNowHoleIndex;
                        ngInfo.后治具生产批次号 = nowFixture.治具生产批次号;

                        //检测是第一次替换还是再次替换刷新数据 最好规定下位机只允许发送替换一次
                        bool bFirstTH = false;
                        string condition2 = $@"物料生产批次号 = '{ngInfo.物料生产批次号}' and 设备id = {ngInfo.设备id} 
                                            and 物料guid = '{ngInfo.物料guid}' and 替换前治具guid = '{ngInfo.替换前治具guid}'
                                            and 替换前治具rfid = '{ngInfo.替换前治具rfid}' and 替换前治具孔号 = {ngInfo.替换前治具孔号}
                                            and 前治具生产批次号 = '{ngInfo.前治具生产批次号}' and 替换后治具guid = '{ngInfo.替换后治具guid}'
                                            and 替换后治具rfid = '{ngInfo.替换后治具rfid}' and 替换后治具孔号 = {ngInfo.替换后治具孔号}
                                            and 后治具生产批次号 = '{ngInfo.后治具生产批次号}'";
                        var var3 = BLLFactory<物料ng替换记录表>.Instance.FindLast(condition2);
                        if (var3 == null)
                        {
                            bFirstTH = true;
                        }
                        if (bFirstTH)
                        {
                            preFixture.RemoveMaterialItem(thMaterialItem);
                            nowFixture.RemoveMaterialItem(ngMaterialItem);
                            nowFixture.InsertMaterialItem(iNowHoleIndex, thMaterialItem);

                            BLLFactory<物料ng替换记录表>.Instance.Insert(ngInfo);  //写入一条数据到数据库中
                        }
                        else
                        {
                            BLLFactory<物料ng替换记录表>.Instance.Update(ngInfo, var3.Ng替换记录id);
                        }

                        dataSend = Encoding.UTF8.GetBytes("NGTHOK"); //返回下位机"NG替换完成"
                        listener.SendMesAsyncToClient(client, dataSend);
                        break;
                    case Header.XT:  //心跳

                        break;
                    case Header.TL:  //投料

                        break;
                }

                ItemManager.Instance.Save(); //每通讯一次就保存一次内存数据
            }
            catch (Exception ex)
            {
                LogService.Warn(ex.Message, ex);
            }
        }
    }

    //定义协议头
    public enum Header  
    {
        CX,
        XR,
        NGTH,
        XT,
        TL
    }
}
