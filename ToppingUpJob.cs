using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTechService.DataModel;
using WinTechService.Helpers;
using WinTechService.Models.Enums;

namespace WinTechService.Models.Services
{
    /// <summary>
    /// Контроль превышения доливов
    /// </summary>
    public class ToppingUpJob : IJob
    {
        readonly Topshelf.Logging.NLogLogWriter logger = new Topshelf.Logging.NLogLogWriter(NLog.LogManager.GetCurrentClassLogger(), "ToppingUpJob");

        public async Task Execute(IJobExecutionContext context)
        {
            Do();

            await Task.CompletedTask; // https://stackoverflow.com/questions/25191512/async-await-return-task
        }

        public void Do()
        {
            /*+ Разработать алгоритм фиксации превышения долива масла в узел за период
         и сохранять исторические данные по превышениям доливов.
             1. Получить стартовую дату - СТ (дату последнего ТО КСР или дату последнего превышения)
             2. Подсчитать сумму литров доливов по контролируемым узлам от СТ до ТекущВремени - СМ
             3. Сравнить СМ с показателем нормы из справочника норм доливов масла в узлы
             4. Если есть превышение сформировать запись.
             5. Повторить 1-4.	*/

            try
            {
                var deviceWorkGroupMain = (int)DeviceWorkGroupEnums.Main;

                using (var db = new TechServiceEntities())
                {
                    var SystemPersonalAccountValue = AppParameters.SystemPersonalAccountValue;
                    var personalID = db.Personals.First(x => x.PersonalCompanyNumber == SystemPersonalAccountValue).ID;


                    var deviceWorkGroup = db.DeviceWorkGroups.First(x => x.Code == deviceWorkGroupMain);
                    // Получить анализируемый список бортов и это ГТУ
                    var deviceWorkGroupDevices = db.DeviceWorkGroupDevices.Where(x => x.DeviceWorkGroupID == deviceWorkGroup.ID).ToList();
                    if (deviceWorkGroupDevices.Any()) // В списке что то есть
                    {
                        foreach (var dwg in deviceWorkGroupDevices)
                        {
                            try
                            {
                                var deviceID = dwg.DeviceID;
                                var asdDeviceID = dwg.Device.ASDDeviceID;
                                var deviceModelID = dwg.Device.Model;

                                if (asdDeviceID == null) // Пропуск если вдруг ASDDeviceID не указано
                                    continue;

                                //  1. Получить стартовую дату - СТ (дату последнего ТО КСР или дату последнего превышения)
                                DateTime? startDate = null;

                                var lastDeviceToppingUp = db.DeviceToppingUps.Where(x => x.DeviceID == deviceID).OrderByDescending(x => x.AtTime).FirstOrDefault();
                                if (lastDeviceToppingUp != null)
                                {
                                    // startDate = lastDeviceToppingUp.AtTime;
                                }

                                KSRMaintenance lastAktTO = null;
                                if (asdDeviceID != null)
                                {
                                    lastAktTO = db.KSRMaintenances.Where(x => x.ASDDeviceID == asdDeviceID).OrderByDescending(x => x.DateStart).FirstOrDefault();
                                }

                                if (lastAktTO != null && lastDeviceToppingUp != null) // Имеется и АКТ ТО и превышение долива
                                {
                                    if (lastAktTO.DateStart > lastDeviceToppingUp.AtTime)
                                    {
                                        startDate = lastAktTO.DateStart;
                                    }
                                    else
                                    {
                                        startDate = lastDeviceToppingUp.AtTime;
                                    }
                                }
                                else if (lastAktTO == null && lastDeviceToppingUp != null)
                                {
                                    startDate = lastDeviceToppingUp.AtTime;
                                }
                                else if (lastAktTO != null && lastDeviceToppingUp == null)
                                {
                                    startDate = lastAktTO.DateStart;
                                }
                                else if (lastAktTO == null && lastDeviceToppingUp == null)
                                {
                                    // Не было ни Акта ТО ни превышения, то есть ничего не делаем
                                }

                                // Если стартовая дата поиска Актов ДОЛИВА определена то и ищем их
                                if (startDate != null)
                                {
                                    var now = DateTime.Now;
                                    // Получить все акты доливов в диапазоне и выбрать из них доливы по борту
                                    var ksrToppingUps = db.KSRToppingUps.Where(x => x.DateStart >= startDate && x.DateStart <= now
                                                        && x.KSRToppingupDetails.FirstOrDefault().ASDDeviceID == asdDeviceID).ToList();
                                    if (ksrToppingUps.Any())
                                    {
                                        var totalsQuantity = new List<TUItem>();
                                        // 2.Подсчитать сумму литров доливов по контролируемым узлам от СТ до ТекущВремени -СМ
                                        foreach (var ksrToppingUpItem in ksrToppingUps)
                                        {
                                            foreach (var ksrToppingupDetailItem in ksrToppingUpItem.KSRToppingupDetails)
                                            {
                                                var isValidNodeAndMaterial = db.DeviceModelTUNorms.FirstOrDefault(x => x.KSRNodeTypeID == ksrToppingupDetailItem.KSRNodeTypeID
                                                                                && x.DeviceModelTUNMaterials.FirstOrDefault().KSRMaterialTypeID == ksrToppingupDetailItem.KSRMaterialTypeID);

                                                if (isValidNodeAndMaterial == null) // Игнорируем эту комбинацию
                                                    continue;

                                                var isTotalQuantity = totalsQuantity.FirstOrDefault(x => x.KSRNodeTypeID == ksrToppingupDetailItem.KSRNodeTypeID &&
                                                                               x.KSRMaterialTypeID == ksrToppingupDetailItem.KSRMaterialTypeID);


                                                if (isTotalQuantity == null) // Вставить первую запись для суммирования 
                                                {
                                                    var tuItem = new TUItem
                                                    {
                                                        KSRNodeTypeID = ksrToppingupDetailItem.KSRNodeTypeID,
                                                        KSRMaterialTypeID = ksrToppingupDetailItem.KSRMaterialTypeID,
                                                        QuantityByNorm = ksrToppingupDetailItem.Quantity,
                                                        QuantityTotal = isValidNodeAndMaterial.Quantity,
                                                        Unit = isValidNodeAndMaterial.Unit
                                                    };
                                                    totalsQuantity.Add(tuItem);
                                                }
                                                else // Прибавить расход материала
                                                {
                                                    isTotalQuantity.QuantityByNorm += ksrToppingupDetailItem.Quantity;
                                                }
                                            }
                                        }

                                        //Сохранить полученные превышения в БД
                                        if (totalsQuantity.Any())
                                        {
                                            foreach (var totalsQuantityItem in totalsQuantity)
                                            {
                                                // Да сохранить есть превышение за период
                                                if (totalsQuantityItem.QuantityTotal > totalsQuantityItem.QuantityByNorm)
                                                {
                                                    db.DeviceToppingUps.Add(new DeviceToppingUp
                                                    {
                                                        AtTime = now,
                                                        DeviceID = deviceID,
                                                        ID = Guid.NewGuid(),
                                                        QuantityNorma = int.Parse(Math.Round(totalsQuantityItem.QuantityByNorm, 0).ToString()),
                                                        QuantityAlert = int.Parse(Math.Round(totalsQuantityItem.QuantityTotal, 0).ToString()),
                                                        KSRNodeTypeID = totalsQuantityItem.KSRNodeTypeID,
                                                        TUSourceTypeID = (int)TUSourceTypeEnums.Auto,
                                                        PersonalID = personalID,
                                                        StartDate = startDate.Value,
                                                        EndDate = now
                                                    });
                                                }
                                            }
                                        }

                                        db.SaveChanges();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex);
                            }
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }

    }

    /// <summary>
    /// Суммарные данные по доливам в узел
    /// </summary>
    public sealed class TUItem
    {
        public int KSRNodeTypeID { get; set; }

        public int KSRMaterialTypeID { get; set; }

        public decimal QuantityTotal { get; set; }


        public string Unit { get; set; }

        public decimal QuantityByNorm { get; set; }
    }
}
