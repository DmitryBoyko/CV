using GJob.DataModel;
using GJob.Models.Enums;
using GJob.Models.Internals;
using GJob.Models.Views;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace GJob.Models.Repositories
{
    public interface IEmailRepository
    {
        EmailView GetEmail(Guid id, bool includeFileData, bool includeSupportCall);

        Guid? AddEmailToSupportCall(Guid id, string body, string subject, DateTime created, int personalID, Guid supportCallID);

        bool SetEmailState(Guid emailID, EmailStateTypeEnums state, int personalID, DateTime atTime);

        int GetNextEmailNumber();

        bool SetPersonalEmailConfirmation(Guid emailID, DateTime atTime, int personalID, int departmentID, bool isSign);

        bool RemoveEmailConfirmation(Guid id);

        bool DeleteEmailSupportCall(Guid emailID);

        bool UploadFile(Guid newEmailFileID, Guid emailID, string filename, string extension, byte[] fileObject, string DiskPathToFile, bool isDisk, DateTime created, int PersonalID);

        int GetEmailConfirmationCount(Guid emailID);

        bool IsEmailSupportCallReadyToConfirm(Guid emailID);

        bool SetConfirmEmailSupportCall(Guid emailID, bool isConfirmed);

        IEnumerable<NameValue> GetOrganizationTemplaEmails(int organizationID);

        bool AddEmailConfirmationNote(Guid emailID, string note, int personalID, DateTime dt, Guid? emailConfirmationNoteID);

        bool RemoveEmailConfirmationNote(Guid id);

        bool UpdateEmailConfirmationNote(Guid id, string note);

        IEnumerable<EmailConfirmationNoteView> GetAllEmailConfirmationNotes(Guid emailID);

        IEnumerable<EmailFileView> GetAllEmailFIles(Guid emailID, bool includeFileData);

        bool DeleteEmailFile(Guid emailFileID);

        EmailFileView GetEmailFile(Guid emailFileID);
        bool UpdateEmail(Guid id, string subject, string body);

        bool SaveSMS(List<int> smsPersonal, string smsText, DateTime now, int personalID);

        DeviceWorkGroupEmailView GetDeviceWorkGroupEmail(Guid id);

        bool UpdateSentInfo(Guid emailID, List<string> emails, string emailCopy, string fromEmail, DateTime dt);

        IEnumerable<EmailView> GetEmails(out int totalRecords,
                DateTime? timeStart, DateTime? timeEnd, string search, int? confirmStateID, int? stateTypeID, int? limitOffset, int? limitRowCount, string orderBy, bool desc);

        bool UpdateEmail(Guid id, string subject, string body, string fromEmail, List<string> toEmails,
            string copiesEmails, List<int> smsPersonal, string smsText);

        /// <summary>
        /// Удалить все согласования письма
        /// </summary>
        /// <param name="emailID"></param>
        /// <returns></returns>
        bool ResetConfirms(Guid emailID);
    }

    public class EmailRepository : IEmailRepository
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public IEnumerable<EmailView> GetEmails(out int totalRecords,
                DateTime? timeStart, DateTime? timeEnd, string search, int? confirmStateID, int? stateTypeID, int? limitOffset, int? limitRowCount, string orderBy, bool desc)
        {
            var r = new List<EmailView>();

            using (var db = new GJobEntities())
            {
                var query = db.Emails.AsQueryable();

                if (timeStart != null && timeEnd != null)
                {
                    query = query.Where(p => p.Created >= timeStart && p.Created <= timeEnd);
                }

                if (stateTypeID != null && stateTypeID > -1)
                {
                    /*
                    ID	Name	Code	IsEnabled
                    1	Ожидание отправки	PendingToSend                                     	1
                    2	Отправлено	Sent                                              	1
                    3	Отменено	Canceled                                          	1
                    4	Удалено	Deleted                                           	1
                    5	Ошибка	Error                                             	1 
                    */
                    query = query.Where(p => p.EmailStates.OrderByDescending(x => x.AtTime).FirstOrDefault().EmailStateTypeID == stateTypeID);
                }


                if (confirmStateID != null && confirmStateID > -1)
                {
                    var boolValue = confirmStateID == 1 ? true : false;
                    query = query.Where(p => p.IsConfirmed == boolValue);
                }

                if (!string.IsNullOrEmpty(search))
                {
                    search = search.ToLower();
                    query = query.Where(p => (p.Subject + " " + p.CopiesEmails + " " + p.ToEmails + " " + p.FromEmail + " " + p.Body)
                                        .ToLower().Contains(search));
                }
                totalRecords = query.Count();
                query = query.OrderByDescending(p => p.Created);
                if (limitOffset.HasValue)
                {
                    query = query.Skip(limitOffset.Value).Take(limitRowCount.Value);
                }
                var items = query.ToList(); // Получаем все

               

                foreach (var item in items)
                {
                    var n = new EmailView
                    {
                        ID = item.ID,
                        SentTime = item.SentTime,
                        IsConfirmed = item.IsConfirmed,
                        Number = item.Number,
                        Subject = item.Subject,
                        IsDeleted = item.IsDeleted,
                        ToEmails = item.ToEmails,
                        Created = item.Created,
                        CopiesEmails = item.CopiesEmails,
                        FromEmail = item.FromEmail,
                    };

                    Guid? deviceWorkGroupID = null;
                    if (item.SCEmails != null && item.SCEmails.Any())
                    {
                        var deviceID = item.SCEmails.First().SupportCall.DeviceID;                        
                        var selectedDWG = db.DeviceWorkGroupDevices.FirstOrDefault(x => x.DeviceID == deviceID);
                        if (selectedDWG != null)
                        {
                            deviceWorkGroupID = selectedDWG.DeviceWorkGroupID;
                        }
                    }

                    var confirmationTypeID = (int)ConfirmationTypeEnums.EmailsDueSupportCall;
                    // Ищем матрицу согласований по оборудованию  
                    var selectedRule = GetAllConfirmRules().FirstOrDefault(x => x.ConfirmationTypeID == confirmationTypeID && x.DeviceWorkGroupID == deviceWorkGroupID.Value);

                    //test
                    /* if (item.Number == 10)
                    { 
                    
                    }*/

                    if (selectedRule != null && selectedRule.ConfirmationConfigDeparts.Any())
                    {
                        // Получить персонал соответвтующий текущий матрице
                        var cSigns = selectedRule.ConfirmationConfigDeparts.OrderBy(x => x.SortOrder).ToList();
                        foreach (var cdItem in cSigns)
                        {
                            string personalShortName = "";

                            foreach (var depPersonal in cdItem.ConfirmationConfigDepartPersonals)
                            {
                                var isExisting = item.EmailConfirmations.FirstOrDefault(x => x.PersonalID == depPersonal.PersonalID);
                                if (isExisting != null) // Нормальная ситуация 
                                {
                                    personalShortName = depPersonal.PersonalShortName;
                                    break;
                                }
                            }

                            string deparHtmlRow = string.Format("{0}: <span class='text-primary'>{1}</span>", cdItem.DepartmentName.Trim(), personalShortName);
                            n.Confirmations.Add(deparHtmlRow);
                        }

                        // Получить персонал не соответствующий текущей матрице согласований
                        /* var personalByOldCOnfrimConfigs = new List<string>();
                        foreach (var emailConfirmationItem in item.EmailConfirmations)
                        {
                            foreach (var cdItem in selectedRule.ConfirmationConfigDeparts.OrderBy(x => x.DepartmentName))
                            {
                                foreach (var depPersonal in cdItem.ConfirmationConfigDepartPersonals)
                                {
                                    if (depPersonal.PersonalID != emailConfirmationItem.PersonalID)
                                    {
                                        personalByOldCOnfrimConfigs.Add(depPersonal.PersonalShortName);
                                    }
                                }
                            }
                        }
                        if (personalByOldCOnfrimConfigs.Any())
                        {
                            foreach (var personalByOldCOnfrimConfigItem in personalByOldCOnfrimConfigs)
                            {
                                string deparHtmlRow = string.Format("Не определно: {0}", personalByOldCOnfrimConfigItem);
                                n.Confirmations.Add(deparHtmlRow);
                            }
                        }*/
                    }

                    foreach (var emailStateItem in item.EmailStates)
                    {
                        var s = new EmailStateView
                        {
                            ID = emailStateItem.ID,
                            AtTime = emailStateItem.AtTime,
                            EmailID = emailStateItem.EmailID,
                            PersonalID = emailStateItem.PersonalID,
                            EmailStateTypeID = emailStateItem.EmailStateTypeID,
                            EmailStateTypeName = emailStateItem.EmailStateType.Name.Trim(),
                            PersonalShortName = emailStateItem.Personal.LastName + " " + emailStateItem.Personal.FirstName.Substring(0, 1) + " " +
                               emailStateItem.Personal.MiddleName.Substring(0, 1) + "."
                        };
                        n.EmailStates.Add(s);
                    }

                    if (item.SCEmails != null && item.SCEmails.Any())
                    {
                        var link = item.SCEmails.FirstOrDefault();
                        if (link != null)
                        {
                            var org = db.SupportCallOrganizationDetails.FirstOrDefault(x => x.SupportCallID == link.SupportCallID);
                            if (org != null && org.Organization != null)
                            {
                                n.ToOrganizationNames.Add(org.Organization.Name.Trim());
                            }

                            try
                            {
                                var deviceName = org.SupportCall.Device.DeviceModel.Name.Trim() + " № " + org.SupportCall.Device.Name.Trim();
                                n.LinkedDeviceName = deviceName;
                            }
                            catch
                            {

                            }
                        }
                    }

                    r.Add(n);
                }
            }

            return r;
        }

        public bool SetPersonalEmailConfirmation(Guid emailID, DateTime atTime, int personalID, int departmentID, bool isSign)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    if (isSign) // Подписать
                    {
                        var isExisting = db.EmailConfirmations.FirstOrDefault(x => x.EmailID == emailID && x.PersonalID == personalID && x.DepartmentID == departmentID);
                        if (isExisting == null)
                        {
                            db.EmailConfirmations.Add(new EmailConfirmation
                            {
                                ID = Guid.NewGuid(),
                                EmailID = emailID,
                                AtTime = atTime,
                                PersonalID = personalID,
                                DepartmentID = departmentID
                            });

                            db.SaveChanges();
                        }
                    }
                    else // Отменить подписание
                    {
                        // Подписей может быть много, например много подписей одного и того же лица. Удаляем по одной записи.
                        var isExisting = db.EmailConfirmations.FirstOrDefault(x => x.EmailID == emailID && x.PersonalID == personalID && x.DepartmentID == departmentID);
                        if (isExisting != null) // Уже подписано удалить
                        {
                            db.EmailConfirmations.Remove(isExisting);
                            db.SaveChanges();
                        }
                    }

                    if (IsEmailSupportCallReadyToConfirm(emailID))
                    {
                        var email = db.Emails.First(x => x.ID == emailID);
                        db.Events.Add(new Event
                        {
                            AtTime = atTime,
                            ID = Guid.NewGuid(),
                            IsDone = false,
                            EventTypeID = (int)EventTypeEnums.EmailConfimOK,
                            Link = emailID.ToString(),
                            DataContainer = JsonConvert.SerializeObject(new EventMsgEmailModel
                            {
                                ID = emailID,
                                Body = email.Body,
                                Created = email.Created,
                                Number = email.Number,
                                Subject = email.Subject,
                            }, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })

                        });

                        db.SaveChanges();
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public Guid? AddEmailToSupportCall(Guid id, string body, string subject, DateTime created, int personalID, Guid supportCallID)
        {
            try
            {
                var number = GetNextEmailNumber();

                using (var db = new GJobEntities())
                {
                    var n = new Email()
                    {
                        ID = id,
                        Body = body,
                        Created = created,
                        Subject = subject,

                        IsDeleted = false, // Не удалено
                        Number = number,
                    };

                    n.IsConfirmed = false; // Не согласовано

                    n.EmailStates.Add(new EmailState
                    {
                        ID = Guid.NewGuid(),
                        EmailID = id,
                        AtTime = created,
                        PersonalID = personalID,
                        EmailStateTypeID = (int)EmailStateTypeEnums.PendingToSend
                    });

                    n.SCEmails.Add(new SCEmail()
                    {
                        ID = Guid.NewGuid(),
                        EmailID = id,
                        PersonalID = personalID,
                        SupportCallID = supportCallID
                    });

                    db.Emails.Add(n);

                    // Зная что этот метод исключительно для заявок на вызов орг-ии AddEmailToSupportCall
                    // определеяем EventTypeEnums поэтому не используем ConfirmationTypes

                    db.Events.Add(new Event
                    {
                        AtTime = created,
                        ID = Guid.NewGuid(),
                        IsDone = false,
                        EventTypeID = (int)EventTypeEnums.EmailCreateAndReadyConfirm,
                        Link = id.ToString(),
                        DataContainer = JsonConvert.SerializeObject(new EventMsgEmailModel
                        {
                            ID = id,
                            Body = body,
                            Created = created,
                            Number = number,
                            Subject = subject,
                        }, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })
                    });

                    db.SaveChanges();

                    return n.ID;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return null;
        }

        public bool DeleteEmailSupportCall(Guid emailID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var email = db.Emails.FirstOrDefault(x => x.ID == emailID);
                    if (email != null)
                    {
                        db.Emails.Remove(email);
                        db.SaveChanges();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public EmailView GetEmail(Guid id, bool includeFileData, bool includeSupportCall)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var email = db.Emails.FirstOrDefault(x => x.ID == id);
                    if (email == null) return null;

                    var n = new EmailView()
                    {
                        ID = email.ID,
                        Body = email.Body,
                        Created = email.Created,
                        IsDeleted = email.IsDeleted,
                        Number = email.Number,
                        Subject = email.Subject,
                        IsConfirmed = email.IsConfirmed,
                        CopiesEmails = email.CopiesEmails,
                        FromEmail = email.FromEmail,
                        SentTime = email.SentTime,
                        ToEmails = email.ToEmails,
                        SMSText = email.SMSText,
                        SMSPersonalIDs = email.SMSPersonalIDs
                    };

                    if (email.SCEmails.Any())
                    {
                        var supportCallLink = email.SCEmails.First();
                        n.SCEmail = new SCEmailView()
                        {
                            ID = supportCallLink.ID,
                            EmailID = supportCallLink.EmailID,
                            PersonalID = supportCallLink.PersonalID,
                            SupportCallID = supportCallLink.SupportCallID,
                            PersonalShortName = supportCallLink.Personal.LastName + " " +
                               supportCallLink.Personal.FirstName.Substring(0, 1) + "." +
                               supportCallLink.Personal.MiddleName.Substring(0, 1) + "."
                        };

                        if (includeSupportCall)
                        {
                            var sp = GetSupportCall(supportCallLink.SupportCallID);
                            n.SCEmail.SupportView = sp;
                        }
                    }

                    if (email.EmailStates.Any())
                    {
                        foreach (var emailState in email.EmailStates.OrderByDescending(x => x.AtTime))
                        {
                            n.EmailStates.Add(new EmailStateView
                            {
                                ID = emailState.ID,
                                AtTime = emailState.AtTime,
                                EmailID = emailState.EmailID,
                                EmailStateTypeID = emailState.EmailStateTypeID,
                                PersonalID = emailState.PersonalID,
                                EmailStateTypeName = emailState.EmailStateType.Name.Trim(),
                                PersonalShortName = emailState.Personal.LastName + " " +
                                                       emailState.Personal.FirstName.Substring(0, 1) + "." +
                                                       emailState.Personal.MiddleName.Substring(0, 1) + "."
                            });
                        }
                    }

                    if (email.EmailConfirmations.Any())
                    {
                        foreach (var emailConfirmation in email.EmailConfirmations)
                        {
                            n.EmailConfirmations.Add(new EmailConfirmationView
                            {
                                ID = emailConfirmation.ID,
                                AtTime = emailConfirmation.AtTime,
                                EmailID = emailConfirmation.EmailID,
                                PersonalID = emailConfirmation.PersonalID,
                                PersonalShortName = emailConfirmation.Personal.LastName + " " +
                                                       emailConfirmation.Personal.FirstName.Substring(0, 1) + "." +
                                                       emailConfirmation.Personal.MiddleName.Substring(0, 1) + ".",
                                Position = emailConfirmation.Personal.Position,
                                DepartmentID = emailConfirmation.DepartmentID
                            });
                        }
                    }

                    if (email.EmailConfirmationNotes.Any())
                    {
                        foreach (var emailConfirmationNote in email.EmailConfirmationNotes.OrderBy(x => x.AtTime))
                        {
                            n.EmailConfirmationNotes.Add(new EmailConfirmationNoteView
                            {
                                ID = emailConfirmationNote.ID,
                                AtTime = emailConfirmationNote.AtTime,
                                EmailID = emailConfirmationNote.EmailID,
                                PersonalID = emailConfirmationNote.PersonalID,
                                EmailConfirmationNoteID = emailConfirmationNote.EmailConfirmationNoteID,
                                Note = emailConfirmationNote.Note,
                                PersonalShortName = emailConfirmationNote.Personal.LastName + " " +
                                                       emailConfirmationNote.Personal.FirstName.Substring(0, 1) + "." +
                                                       emailConfirmationNote.Personal.MiddleName.Substring(0, 1) + "."
                            });
                        }
                    }

                    if (email.EmailFiles.Any())
                    {
                        foreach (var emailFile in email.EmailFiles)
                        {
                            var file = new EmailFileView()
                            {
                                ID = emailFile.ID,
                                IsDisk = emailFile.IsDisk,
                                PersonalID = emailFile.PersonalID,
                                Created = emailFile.Created,
                                EmailID = emailFile.EmailID,
                                DiskPathToFile = emailFile.DiskPathToFile,
                                Extension = emailFile.Extension,
                                Filename = emailFile.Filename,
                                PersonalShortName = emailFile.Personal.LastName + " " +
                                                       emailFile.Personal.FirstName.Substring(0, 1) + "." +
                                                       emailFile.Personal.MiddleName.Substring(0, 1) + "."
                            };

                            if (includeFileData)
                            {
                                file.FileObject = emailFile.FileObject;
                            }

                            n.EmailFiles.Add(file);
                        }
                    }

                    #region Получить ФИО людей которым отправили СМС

                    if (n.SMSPersonalIDsAsList != null && n.SMSPersonalIDsAsList.Any())
                    {
                        foreach (var personalItem in n.SMSPersonalIDsAsList)
                        {
                            int.TryParse(personalItem, out int personalIDValue);
                            var p = db.PersonalViews.FirstOrDefault(x => x.ID == personalIDValue);
                            if (p != null)
                            {
                                n.SMSPersonalShortNames.Add(p.FullName);
                            }
                        }
                    }

                    #endregion

                    return n;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return null;
        }

        public int GetEmailConfirmationCount(Guid emailID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var count = db.EmailConfirmations.Count(x => x.EmailID == emailID);
                    return count;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return 0;
        }

        public int GetNextEmailNumber()
        {
            using (var db = new GJobEntities())
            {
                var email = db.Emails.OrderByDescending(x => x.Created).FirstOrDefault();
                if (email != null)
                {
                    if (email.Created.Year == DateTime.Now.Year) // Продолжаем сквозную нумерацию в течение года
                    {
                        return email.Number + 1;
                    }
                    // Годы отличаются, пришел новый год, начинаем нумерацию с единицы
                }
            }

            return 1;
        }

        public bool IsEmailSupportCallReadyToConfirm(Guid emailID)
        {
            try
            {
                var confirmConfigs = GetAllConfirmRules();
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == emailID);
                    if (isExisting != null)
                    {
                        // Всего пользователей
                        var count = isExisting.EmailConfirmations.Count();

                        //Получить произв. группу
                        if (isExisting.SCEmails.Any())
                        {
                            var deviceWorkGroupDevice = isExisting.SCEmails.First().SupportCall.Device.DeviceWorkGroupDevices.FirstOrDefault();
                            if (deviceWorkGroupDevice != null)
                            {
                                //Получить сколько положено по правилам согласования
                                var rule = confirmConfigs.FirstOrDefault(x => x.DeviceWorkGroupID == deviceWorkGroupDevice.DeviceWorkGroupID);
                                if (rule != null)
                                {
                                    //Если больше или равно чем по правилам то ок иначе нет
                                    if (count >= rule.PersonalPerDepartNumbers)
                                    {
                                        return true;
                                    }
                                    else
                                    {
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool RemoveEmailConfirmation(Guid id)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.EmailConfirmations.FirstOrDefault(x => x.ID == id);
                    if (isExisting != null)
                    {
                        db.EmailConfirmations.Remove(isExisting);
                        db.SaveChanges();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool SetEmailState(Guid emailID, EmailStateTypeEnums state, int personalID, DateTime atTime)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    db.EmailStates.Add(new EmailState
                    {
                        ID = Guid.NewGuid(),
                        EmailID = emailID,
                        AtTime = atTime,
                        PersonalID = personalID,
                        EmailStateTypeID = (int)state
                    });

                    if (state == EmailStateTypeEnums.Sent)
                    {
                        var email = db.Emails.FirstOrDefault(x => x.ID == emailID);
                        if (email != null)
                        {
                            email.SentTime = atTime;
                        }
                    }

                    db.SaveChanges();

                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool UploadFile(Guid newEmailFileID, Guid emailID, string filename, string extension, byte[] fileObject, string DiskPathToFile, bool isDisk, DateTime created, int PersonalID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var n = new EmailFile()
                    {
                        Created = created,
                        DiskPathToFile = DiskPathToFile,
                        EmailID = emailID,
                        Extension = extension,
                        Filename = filename,
                        ID = newEmailFileID,
                        IsDisk = isDisk,
                        FileObject = fileObject,
                        PersonalID = PersonalID,
                    };

                    db.EmailFiles.Add(n);

                    db.SaveChanges();

                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public IEnumerable<ConfirmationConfigView> GetAllConfirmRules()
        {
            var r = new List<ConfirmationConfigView>();

            try
            {
                using (var db = new GJobEntities())
                {
                    var rules = db.ConfirmationConfigs.ToList();

                    foreach (var rule in rules)
                    {
                        var n = new ConfirmationConfigView()
                        {
                            ID = rule.ID,
                            ConfirmationTypeID = rule.ConfirmationTypeID,
                            ConfirmationTypeName = rule.ConfirmationType.Name.Trim(),
                            DepartmentNumbers = rule.DepartmentNumbers,
                            PersonalPerDepartNumbers = rule.PersonalPerDepartNumbers,
                            Code = rule.Code,
                            DeviceWorkGroupID = rule.DeviceWorkGroupID,
                            Name = rule.Name,
                            DeviceWorkGroupName = rule.DeviceWorkGroup.Name.Trim()
                        };

                        foreach (var department in rule.ConfirmationConfigDeparts)
                        {
                            var d = new ConfirmationConfigDepartView()
                            {
                                ID = department.ID,
                                DepartmentID = department.DepartmentID,
                                ConfirmationConfigID = department.ConfirmationConfigID,
                                DepartmentName = department.Department.Name.Trim(),
                                SortOrder = (department.SortOrder == null ? 0 : department.SortOrder.Value)
                            };

                            foreach (var personal in department.ConfirmationConfigDepartPersonals)
                            {
                                var p = new ConfirmationConfigDepartPersonalView()
                                {
                                    ID = personal.ID,
                                    ConfirmationConfigDepartID = personal.ConfirmationConfigDepartID,
                                    PersonalID = personal.PersonalID,
                                    LastName = personal.Personal.LastName.Trim(),
                                    FirstName = personal.Personal.FirstName.Trim(),
                                    MiddleName = personal.Personal.MiddleName.Trim()
                                };

                                d.ConfirmationConfigDepartPersonals.Add(p);
                            }

                            n.ConfirmationConfigDeparts.Add(d);
                        }

                        r.Add(n);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return r;
        }

        public bool SetConfirmEmailSupportCall(Guid emailID, bool isConfirmed)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == emailID);
                    if (isExisting != null)
                    {
                        isExisting.IsConfirmed = isConfirmed;
                        db.SaveChanges();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        private SuppotCallView GetSupportCall(Guid id)
        {
            try
            {
                SuppotCallView m;
                using (var db = new GJobEntities())
                {
                    var supportCall = db.SupportCalls.FirstOrDefault(x => x.ID == id);

                    m = new SuppotCallView()
                    {
                        ID = supportCall.ID,
                        CompletedByPersonID = supportCall.CompletedByPersonID,
                        CompletedChangedTime = supportCall.CompletedChangedTime,
                        ConfirmedByPersonalID = supportCall.ConfirmedByPersonalID,
                        ConfirmedChangedTime = supportCall.ConfirmedChangedTime,
                        IsNeedExternal = supportCall.IsNeedExternal,
                        NeedExternalPersonalID = supportCall.NeedExternalPersonalID,
                        IsNeedPersonalChangedTine = supportCall.IsNeedPersonalChangedTine,
                        Created = supportCall.Created,
                        DeviceID = supportCall.DeviceID,
                        FromPerson = supportCall.FromPerson,

                        IsConfirmed = supportCall.IsConfirmed,
                        IsDeleted = supportCall.IsDeleted,
                        Note = supportCall.Note,
                        PersonID = supportCall.PersonID,
                        DeviceFullName = supportCall.Device.DeviceModel.Name.Trim() + " # " + supportCall.Device.Name.Trim(),
                        PersonFullName = supportCall.Personal.LastName.Trim() + " " + supportCall.Personal.FirstName.Substring(0, 1) + "." + supportCall.Personal.MiddleName.Substring(0, 1) + ".",
                        IsWarranty = supportCall.IsWarranty
                    };

                    if (supportCall.SupportCallPlanStartTimes.Where(x => x.IsDeleted == false).Any())
                    {
                        foreach (var planTimeitem in supportCall.SupportCallPlanStartTimes.Where(x => x.IsDeleted == false))
                        {
                            m.SupportCallPlanStartTimes.Add(new SuppotCallView.SupportCallPlanStartTimeView
                            {
                                ID = planTimeitem.ID,
                                IsDeleted = planTimeitem.IsDeleted,
                                PersonalFullName = planTimeitem.Personal.LastName + " " + planTimeitem.Personal.FirstName.Substring(0, 1) + "." +
                                    planTimeitem.Personal.MiddleName.Substring(0, 1) + ".",
                                PersonalID = planTimeitem.PersonalID,
                                PlanStartTime = planTimeitem.PlanStartTime,
                                SupportCallID = planTimeitem.SupportCallID,
                                Updated = planTimeitem.Updated
                            });
                        }
                    }

                    if (supportCall.SupportCallWorks.Any())
                    {
                        foreach (var wItem in supportCall.SupportCallWorks)
                        {
                            m.SupportCallWorks.Add(new SuppotCallView.SupportCallWorkView
                            {
                                Created = wItem.Created,
                                FactEndTime = wItem.FactEndTime,
                                FactStartTime = wItem.FactStartTime,
                                ID = wItem.ID,
                                PersonalFullName = wItem.Personal.LastName + " " + wItem.Personal.FirstName.Substring(0, 1) + "." +
                                    wItem.Personal.MiddleName.Substring(0, 1) + ".",
                                PersonalID = wItem.PersonalID,
                                SupportCallID = wItem.SupportCallID
                            });
                        }
                    }

                    if (supportCall.SupportCallEmails.Any())
                    {
                        foreach (var eItem in supportCall.SupportCallEmails)
                        {
                            m.SupportCallEmails.Add(new SupportCallEmailView
                            {
                                EmailBody = eItem.EmailBody,
                                SupportCallID = eItem.SupportCallID,
                                EmailSubject = eItem.EmailSubject,
                                ID = eItem.ID,
                                PersonalID = eItem.PersonalID,
                                SendCopy = eItem.SendCopy,
                                SendTo = eItem.SendTo,
                                SentTime = eItem.SentTime
                            });
                        }
                        m.IsEmails = true;
                    }

                    if (supportCall.SupportCallOrganizationDetails.Any())
                    {
                        foreach (var item in supportCall.SupportCallOrganizationDetails)
                        {
                            m.OrganizationViews.Add(new SupportCallOrganizationDetailView
                            {
                                OrganizationID = item.OrganizationID,
                                Name = item.Organization.Name.Trim(),
                                ID = item.ID,
                                Note = item.Note
                            });
                        }
                    }

                    if (supportCall.ConfirmedByPersonalID != null)
                    {
                        var confirmedPerson = db.Personals.FirstOrDefault(x => x.ID == supportCall.ConfirmedByPersonalID);
                        if (confirmedPerson != null)
                        {
                            m.ConfirmedByPersonalFullName = confirmedPerson.LastName + confirmedPerson.FirstName.Substring(0, 1) + "." + confirmedPerson.MiddleName.Substring(0, 1) + ".";
                        }
                    }

                    if (supportCall.CompletedByPersonID != null)
                    {
                        var completedPerson = db.Personals.FirstOrDefault(x => x.ID == supportCall.CompletedByPersonID);
                        if (completedPerson != null)
                        {
                            m.CompletedByPersonFullName = completedPerson.LastName + completedPerson.FirstName.Substring(0, 1) + "." + completedPerson.MiddleName.Substring(0, 1) + ".";
                        }
                    }

                    if (supportCall.NeedExternalPersonalID != null)
                    {
                        var extPerson = db.Personals.FirstOrDefault(x => x.ID == supportCall.NeedExternalPersonalID);
                        if (extPerson != null)
                        {
                            m.NeedExternalByPersonFullName = extPerson.LastName + extPerson.FirstName.Substring(0, 1) + "." + extPerson.MiddleName.Substring(0, 1) + ".";
                        }
                    }

                    if (supportCall.SupportCallDeviceNodeDetails.Any())
                    {
                        foreach (var item in supportCall.SupportCallDeviceNodeDetails)
                        {
                            var n = new SupportCallDeviceNodeDetailView
                            {
                                ID = item.ID,
                                Note = item.Note,
                                SupportCallID = item.SupportCallID,
                                DeviceComponentID = item.DeviceComponentID,
                                // NodeFullName = GetDeviceComponentPath(item.DeviceComponentID)
                            };

                            m.NodeViews.Add(n);
                        }
                    }

                    if (supportCall.SupportCallRCardDetails.Any())
                    {
                        var supportCallRCardDetail = supportCall.SupportCallRCardDetails.First();
                        m.RCardID = supportCallRCardDetail.RCardID;
                        var rCard = db.RCards.FirstOrDefault(x => x.ID == supportCallRCardDetail.RCardID);
                        m.RCardInfo = rCard.Created.ToString("dd.MM.yy HH:mm") + " - " + rCard.Personal.LastName
                                + " " + rCard.Personal.FirstName.Substring(0, 1) + "." +
                             rCard.Personal.MiddleName.Substring(0, 1) + "." + " - " + (rCard.IsCompleted ? "Закрыта" : "Открыта") + " - " +
                             rCard.MaintenanceType.Name.Trim();
                    }

                    if (supportCall.DeviceDetectedFaultID != null)
                    {
                        m.DDFaultView = GetDDFault(supportCall.DeviceDetectedFaultID.Value);
                    }

                }
                return m;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            return null;
        }

        private DDFaultView GetDDFault(Guid id)
        {
            try
            {
                using (var db = new GJobEntities())
                {

                    var item = db.DeviceDetectedFaults.FirstOrDefault(x => x.ID == id);
                    if (item != null)
                    {
                        var n = new DDFaultView()
                        {
                            ID = item.ID,
                            AtTime = item.AtTime,
                            DefinedByPersonalID = item.DefinedByPersonalID,
                            DeviceID = item.DeviceID,
                            DeviceComponentID = item.DeviceComponentID,
                            DeviceNodeTechStateID = item.DeviceNodeTechStateID,
                            IsDeleted = item.IsDeleted,
                            Name = item.Name,
                            DeviceFullName = item.Device.DeviceModel.Name.Trim() + " № " + item.Device.Name.Trim(),
                            DefinedByOrganizationName = item.Organization.Name.Trim(),
                            DeviceNodeTechStateName = item.DeviceNodeTechState.Name.Trim(),
                            Note = item.Note,
                            IsConfirmed = item.IsConfirmed,
                            ConfirmedByPersonalID = item.ConfirmedByPersonalID,
                        };

                        if (item.Device.DeviceWorkGroupDevices != null && item.Device.DeviceWorkGroupDevices.Any())
                        {
                            var dgw = item.Device.DeviceWorkGroupDevices.First();
                            n.DeviceWorkGroup.Code = dgw.DeviceWorkGroup.Code != null ? dgw.DeviceWorkGroup.Code.Value : 0;
                            n.DeviceWorkGroup.ControllerName = dgw.DeviceWorkGroup.ControllerName;
                            n.DeviceWorkGroup.Name = dgw.DeviceWorkGroup.Name;
                            n.DeviceWorkGroup.IsEnabled = dgw.DeviceWorkGroup.IsEnabled;
                            n.DeviceWorkGroup.SortOrder = dgw.DeviceWorkGroup.SortOrder;
                            n.DeviceWorkGroup.ID = dgw.DeviceWorkGroup.ID;
                        }

                        if (item.ConfirmedByPersonalID != null)
                        {
                            var personal = db.Personals.FirstOrDefault(x => x.ID == item.ConfirmedByPersonalID.Value);
                            n.ConfirmedByPersonalFullName = personal.LastName + " " + personal.FirstName.Substring(0, 1) + "." +
                                 personal.MiddleName.Substring(0, 1) + ".";
                        }

                        var supportCalls = db.SupportCalls.Where(x => x.DeviceDetectedFaultID == item.ID && x.IsDeleted == false).ToList();
                        if (supportCalls.Any())
                        {
                            n.SupportCalls.AddRange(supportCalls.Select(x => new SupportCallView
                            {
                                ID = x.ID,
                                CompletedByPersonID = x.CompletedByPersonID,
                                CompletedChangedTime = x.CompletedChangedTime,
                                ConfirmedByPersonalID = x.ConfirmedByPersonalID,
                                ConfirmedChangedTime = x.ConfirmedChangedTime,
                                Created = x.Created,
                                DeviceDetectedFaultID = x.DeviceDetectedFaultID,
                                DeviceID = x.DeviceID,
                                FromPerson = x.FromPerson,
                                IsConfirmed = x.IsConfirmed,
                                IsDeleted = x.IsDeleted,
                                IsNeedExternal = x.IsNeedExternal,
                                IsNeedPersonalChangedTine = x.IsNeedPersonalChangedTine,
                                NeedExternalPersonalID = x.NeedExternalPersonalID,
                                Note = x.Note,
                                PersonID = x.PersonID,
                                SupportCallStateTypeID = x.SupportCallStateTypeID,
                                SCallOrgDetails = x.SupportCallOrganizationDetails.Select(d => new SCallOrgDetailView
                                {
                                    ID = d.ID,
                                    OrganizationID = d.OrganizationID,
                                    OrganizationName = d.Organization.Name.Trim(),
                                    OrganizationShortName = d.Organization.ShortName.Trim(),
                                    SupportCallID = d.SupportCallID
                                }).ToList()
                            }));
                        }

                        if (item.DefinedByPersonalID != null)
                        {
                            n.DefinedByPersonalFullName = item.Personal.LastName.Trim() + " " + item.Personal.FirstName.Substring(0, 1) + "." +
                                 item.Personal.MiddleName.Substring(0, 1) + ".";
                        }

                        if (item.DeviceComponentID != null)
                        {
                            n.DeviceComponentPath = GetDeviceComponentPath(item.DeviceComponentID.Value);
                        }

                        if (item.DeviceDetectedFaultKSRs.Any())
                        {
                            var ksrMaintenance = item.DeviceDetectedFaultKSRs.FirstOrDefault();
                            if (ksrMaintenance != null)
                            {
                                //TODO нестыковка неисправностей РАЗОБРАТЬСЯ !!!
                                // var deviceMaintenance = GetDeviceMaintenance(ksrMaintenance.KSRMaintenanceID);

                                var trouble = db.KSRMaintenanceTroubles.FirstOrDefault(x => x.ID == ksrMaintenance.KSRMaintenanceTroubleID);

                                n.KSRMaintenanceID = ksrMaintenance.KSRMaintenanceID;
                                n.KSRMaintenanceNumber = ksrMaintenance.KSRMaintenance.Number.Trim();
                                n.KSRMaintenanceTroubleID = ksrMaintenance.KSRMaintenanceTroubleID;
                                n.KSRTroubleID = ksrMaintenance.KSRTroubleID;

                                //TODO BUG Предположитель связано с неудалением ссылки при удалении Акто ТО при импорт в сервисе импорта
                                if (trouble != null)
                                {
                                    n.KSRTroubleTypeName = trouble.KSRTroubleType.Name.Trim();
                                    n.KSRTroublePlaceTypeID = trouble.KSRPlaceTypeID;
                                    n.KSRTroublePlaceTypeName = trouble.KSRPlaceType.Name.Trim();
                                    n.KSRAktTroubleTypeState = trouble.KSRActTroubleType.Name.Trim();
                                    n.KSRRepeat = trouble.Repeat;
                                    n.KSRCode = trouble.KSRCode;
                                }
                                /*else
                                {
                                    var KSRTroubleType = db.KSRActTroubleTypes.FirstOrDefault(x=>x.ID == ksrMaintenance.KSRTroubleID);
                                    if (KSRTroubleType != null)
                                    {
                                        n.KSRTroubleTypeName = KSRTroubleType.Name;
                                    }
                                }*/
                            }
                        }

                        var rf = item.RepairForecastDeviceDFaults.Where(x => x.RepairForecast.IsDeleted == false).FirstOrDefault();
                        if (rf != null)
                        {
                            n.HasConnectionToRepairForecast = true;

                            n.RepairForecastView = new RepairForecastView()
                            {
                                ID = rf.RepairForecast.ID,
                                Created = rf.RepairForecast.Created,
                                DeviceID = rf.RepairForecast.DeviceID,
                                TimeStart = rf.RepairForecast.TimeStart,
                                Note = rf.RepairForecast.Note,
                                MaintenanceTypeID = rf.RepairForecast.MaintenanceTypeID,
                                PersonalID = rf.RepairForecast.PersonalID,
                                IsDeleted = rf.RepairForecast.IsDeleted,
                                DeviceFullName = rf.RepairForecast.Device.DeviceModel.Name + " № " + rf.RepairForecast.Device.Name,
                                MaintenanceTypeName = rf.RepairForecast.MaintenanceType.Name.Trim(),
                                PersonalFullName = rf.RepairForecast.Personal.LastName + " " + rf.RepairForecast.Personal.FirstName.Substring(0, 1) + "." +
                                                                  rf.RepairForecast.Personal.MiddleName.Substring(0, 1) + "."

                            };
                        }

                        /* if (item.RCardContainerID != null)
                        {
                            n.RCardContainerID = item.RCardContainerID.Value;
                            var rCardContainer = db.RCardContainers.FirstOrDefault(x => x.ID == item.RCardContainerID);
                            if (rCardContainer != null)
                            {
                                n.RCardID = rCardContainer.RCardID;
                                n.RCard = GetRCardShortView(rCardContainer.RCardID);
                            }
                        }*/

                        if (item.DeviceDetectedFaultFiles != null && item.DeviceDetectedFaultFiles.Any())
                        {
                            foreach (var file in item.DeviceDetectedFaultFiles)
                            {
                                n.Photos.Add(new DDFaultView.PhotoView
                                {
                                    Created = file.Created,
                                    ID = file.ID.ToString(),
                                    FileObject = file.FileObject,
                                    Extension = file.Extension
                                });
                            }
                        }

                        if (item.DDFaultResponsables != null && item.DDFaultResponsables.Any())
                        {
                            foreach (var p in item.DDFaultResponsables)
                            {
                                var responsible = new DDFaultResponsableView()
                                {
                                    AtTime = p.AtTime,
                                    DeviceDetectedFaultID = p.DeviceDetectedFaultID,
                                    ID = p.ID,
                                    IsDeleted = p.IsDeleted,
                                    PersonalID = p.PersonalID,
                                    PersonalFullName = p.Personal.LastName.Trim() + " " + p.Personal.FirstName.Substring(0, 1) + "."
                                               + p.Personal.MiddleName.Substring(0, 1) + "."
                                };

                                n.DDFaultResponsables.Add(responsible);
                            }
                        }

                        if (item.DDFaultStates != null && item.DDFaultStates.Any())
                        {
                            foreach (var faultStateItem in item.DDFaultStates)
                            {
                                var state = new DDFaultStateView()
                                {
                                    ID = faultStateItem.ID,
                                    AtTime = faultStateItem.AtTime,
                                    DDFaultStateTypeID = faultStateItem.DDFaultStateTypeID,
                                    DDFaultStateTypeName = faultStateItem.DDFaultStateType.Name.Trim(),
                                    DeviceDetectedFaultID = faultStateItem.DeviceDetectedFaultID,
                                    PersonalID = faultStateItem.PersonalID,
                                    PersonalFullName = faultStateItem.Personal.LastName + " " +
                                          faultStateItem.Personal.FirstName.Substring(0, 1) + "." +
                                           faultStateItem.Personal.MiddleName.Substring(0, 1) + "."
                                };
                                n.FaultStates.Add(state);
                            }
                        }

                        // Получить заявки на запчасти
                        /* if (item.DDFaultOrders != null && item.DDFaultOrders.Any())
                        {

                            foreach (var faultOrderItem in item.DDFaultOrders)
                            {
                                var order = GetOrderSimpleEntity(faultOrderItem.OrderID);
                                if (order != null)
                                {
                                    n.Orders.Add(order);
                                }
                            }
                        }*/

                        return n;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return null;
        }

        public string GetDeviceComponentPath(Guid deviceComponentID)
        {
            var fullDeviceComponentName = "";
            try
            {
                using (var db = new GJobEntities())
                {
                    var selectedComponent = db.DeviceComponentsViews.FirstOrDefault(x => x.DeviceComponentID == deviceComponentID);
                    if (selectedComponent != null)
                    {
                        #region Построить иерархию компонента

                        var n = new DeviceComponentItem()
                        {
                            DeviceComponentID = selectedComponent.DeviceComponentID,
                            // BgdColor = (string.IsNullOrEmpty(selectedComponent.BgdColor) ? defaultDeviceNodeColor : selectedComponent.BgdColor.Trim()),
                            IsEnabled = selectedComponent.IsEnabled,
                            IsNode = selectedComponent.IsNode,
                            DeviceNodeParentID = selectedComponent.DeviceNodeParentID,
                            Alias = selectedComponent.Alias,
                            DeviceNodeName = selectedComponent.DeviceNodeName,
                            DeviceID = selectedComponent.DeviceID,
                            DeviceNodeID = selectedComponent.DeviceNodeID,
                        };
                        var sourcePath = new List<DeviceComponentItem>();
                        var allNodes = db.DeviceComponentsViews.Where(x => x.DeviceID == selectedComponent.DeviceID);
                        var nodes = allNodes.Select(c => new DeviceComponentItem()
                        {
                            DeviceComponentID = c.DeviceComponentID,
                            // BgdColor = (string.IsNullOrEmpty(selectedComponent.BgdColor) ? defaultDeviceNodeColor : selectedComponent.BgdColor.Trim()),
                            IsEnabled = c.IsEnabled,
                            IsNode = c.IsNode,
                            DeviceNodeParentID = c.DeviceNodeParentID,
                            Alias = c.Alias,
                            DeviceNodeName = c.DeviceNodeName,
                            DeviceID = c.DeviceID,
                            DeviceNodeID = c.DeviceNodeID,
                        }).ToList();
                        sourcePath = DeviceComponentItemEx.FindSourcePath(n, nodes).Reverse().ToList();

                        #endregion
                        foreach (var item in sourcePath)
                        {
                            fullDeviceComponentName += @"\" + item.DeviceNodeName;
                        }
                        if (fullDeviceComponentName.StartsWith(@"\"))
                        {
                            fullDeviceComponentName = fullDeviceComponentName.Substring(1, fullDeviceComponentName.Length - 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            return fullDeviceComponentName;
        }

        public IEnumerable<NameValue> GetOrganizationTemplaEmails(int organizationID)
        {
            var r = new List<NameValue>();

            using (var db = new GJobEntities())
            {
                var emails = db.OrganizationEmails.Where(x => x.OrganizationID == organizationID);
                if (emails.Any())
                {
                    r = emails.Select(x => new NameValue { Value = x.ID.ToString(), Attr1 = x.Email.Trim(), Name = x.Email + " (" + x.Description + ")" }).ToList();
                }
            }

            return r;
        }

        public bool AddEmailConfirmationNote(Guid emailID, string note, int personalID, DateTime dt, Guid? emailConfirmationNoteID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    db.EmailConfirmationNotes.Add(new EmailConfirmationNote
                    {
                        ID = Guid.NewGuid(),
                        AtTime = dt,
                        EmailID = emailID,
                        Note = note,
                        PersonalID = personalID,
                        EmailConfirmationNoteID = emailConfirmationNoteID
                    });
                    db.SaveChanges();
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool RemoveEmailConfirmationNote(Guid id)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.EmailConfirmationNotes.FirstOrDefault(x => x.ID == id);
                    if (isExisting != null)
                    {
                        db.EmailConfirmationNotes.Remove(isExisting);
                        db.SaveChanges();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool UpdateEmailConfirmationNote(Guid id, string note)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.EmailConfirmationNotes.FirstOrDefault(x => x.ID == id);
                    if (isExisting != null)
                    {
                        isExisting.Note = note;
                        isExisting.AtTime = DateTime.Now;

                        db.SaveChanges();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public IEnumerable<EmailConfirmationNoteView> GetAllEmailConfirmationNotes(Guid emailID)
        {
            var r = new List<EmailConfirmationNoteView>();
            try
            {
                using (var db = new GJobEntities())
                {
                    var rows = db.EmailConfirmationNotes.Where(x => x.EmailID == emailID);
                    foreach (var item in rows)
                    {
                        var n = new EmailConfirmationNoteView()
                        {
                            ID = item.ID,
                            AtTime = item.AtTime,
                            EmailID = item.EmailID,
                            Note = item.Note,
                            PersonalID = item.PersonalID,
                            EmailConfirmationNoteID = item.EmailConfirmationNoteID,
                            PersonalShortName = item.Personal.LastName + " " + item.Personal.FirstName.Substring(0, 1) + "." +
                                item.Personal.MiddleName.Substring(0, 1) + "."
                        };

                        r.Add(n);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return r;
        }

        public IEnumerable<EmailFileView> GetAllEmailFIles(Guid emailID, bool includeFileData)
        {
            var r = new List<EmailFileView>();

            try
            {
                using (var db = new GJobEntities())
                {
                    var emailFiles = db.EmailFiles.Where(x => x.EmailID == emailID);
                    if (emailFiles.Any())
                    {
                        foreach (var emailFile in emailFiles)
                        {
                            var file = new EmailFileView()
                            {
                                ID = emailFile.ID,
                                IsDisk = emailFile.IsDisk,
                                PersonalID = emailFile.PersonalID,
                                Created = emailFile.Created,
                                EmailID = emailFile.EmailID,
                                DiskPathToFile = emailFile.DiskPathToFile,
                                Extension = emailFile.Extension,
                                Filename = emailFile.Filename,
                                PersonalShortName = emailFile.Personal.LastName + " " +
                                                       emailFile.Personal.FirstName.Substring(0, 1) + "." +
                                                       emailFile.Personal.MiddleName.Substring(0, 1) + "."
                            };

                            if (includeFileData)
                            {
                                file.FileObject = emailFile.FileObject;
                            }

                            r.Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return r;
        }

        public bool DeleteEmailFile(Guid emailFileID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.EmailFiles.FirstOrDefault(x => x.ID == emailFileID);
                    if (isExisting != null)
                    {
                        db.EmailFiles.Remove(isExisting);
                        db.SaveChanges();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public EmailFileView GetEmailFile(Guid emailFileID)
        {
            EmailFileView file = null;
            using (var db = new GJobEntities())
            {
                var emailFile = db.EmailFiles.FirstOrDefault(x => x.ID == emailFileID);
                if (emailFile != null)
                {
                    file = new EmailFileView()
                    {
                        ID = emailFile.ID,
                        IsDisk = emailFile.IsDisk,
                        PersonalID = emailFile.PersonalID,
                        Created = emailFile.Created,
                        EmailID = emailFile.EmailID,
                        DiskPathToFile = emailFile.DiskPathToFile,
                        Extension = emailFile.Extension,
                        Filename = emailFile.Filename,
                        PersonalShortName = emailFile.Personal.LastName + " " +
                                                           emailFile.Personal.FirstName.Substring(0, 1) + "." +
                                                           emailFile.Personal.MiddleName.Substring(0, 1) + ".",
                        FileObject = emailFile.FileObject
                    };
                }
            }

            return file;
        }

        public bool UpdateEmail(Guid id, string subject, string body)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == id);
                    if (isExisting != null)
                    {
                        isExisting.Body = body;
                        isExisting.Subject = subject;
                        db.SaveChanges();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool UpdateEmail(Guid id, string subject, string body,
          string fromEmail, List<string> toEmails, string copiesEmails, List<int> smsPersonal, string smsText)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == id);
                    if (isExisting != null)
                    {
                        isExisting.Body = body;
                        isExisting.Subject = subject;

                        if (fromEmail != null)
                        {
                            isExisting.FromEmail = fromEmail;
                        }

                        if (toEmails != null)
                        {
                            isExisting.ToEmails = string.Join(",", toEmails);
                        }
                        else
                        {
                            isExisting.ToEmails = null;
                        }

                        isExisting.CopiesEmails = copiesEmails;

                        if (smsPersonal != null)
                        {
                            isExisting.SMSPersonalIDs = string.Join(",", smsPersonal);
                        }

                        if (smsText != null)
                        {
                            isExisting.SMSText = smsText;
                        }

                        db.SaveChanges();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool SaveSMS(List<int> smsPersonal, string smsText, DateTime now, int personalID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    if (smsPersonal != null && !string.IsNullOrEmpty(smsText))
                    {

                        var canSave = false;
                        foreach (var smsItem in smsPersonal)
                        {
                            var targetPersonal = db.Personals.FirstOrDefault(x => x.ID == smsItem);
                            if (targetPersonal != null && !string.IsNullOrEmpty(targetPersonal.Phones))
                            {
                                var phone = targetPersonal.Phones.Trim();
                                var personalShortName = targetPersonal.LastName + " " + targetPersonal.FirstName.Substring(0, 1) + "."
                                       + targetPersonal.MiddleName.Substring(0, 1) + ".";

                                if (phone.StartsWith("+7") && phone.Length == 12) // +79609351070
                                {
                                    db.SMSMessages.Add(new SMSMessage()
                                    {
                                        ID = Guid.NewGuid(),
                                        Created = now,
                                        Phone = phone,
                                        CreatedByPersonal = personalID,
                                        Message = smsText.Trim(),
                                        SendToPersonalID = smsItem,
                                        SendToPersonFullName = personalShortName
                                    });
                                    canSave = true;
                                }
                                else
                                {
                                    logger.Error("Нарушение формата персонального номера телефона для " + personalShortName);
                                }
                            }
                        }

                        if (canSave)
                        {
                            db.SaveChanges();
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public DeviceWorkGroupEmailView GetDeviceWorkGroupEmail(Guid id)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var sender = db.DeviceWorkGroupEmails.FirstOrDefault(x => x.ID == id);
                    if (sender != null)
                    {
                        return new DeviceWorkGroupEmailView
                        {
                            ID = sender.ID,
                            EmailFrom = sender.EmailFrom,
                            IsEnabled = sender.IsEnabled,
                            DeviceWorkGroupID = sender.DeviceWorkGroupID,
                            Email = sender.Email
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return null;
        }

        public bool UpdateSentInfo(Guid emailID, List<string> emails, string emailCopy, string fromEmail, DateTime dt)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == emailID);
                    if (isExisting != null)
                    {
                        if (!string.IsNullOrEmpty(emailCopy))
                        {
                            isExisting.CopiesEmails = emailCopy;
                        }

                        if (!string.IsNullOrEmpty(fromEmail))
                        {
                            isExisting.FromEmail = fromEmail;
                        }

                        if (emails != null && emails.Any())
                        {
                            isExisting.ToEmails = string.Join(",", emails);
                        }

                        isExisting.SentTime = dt;

                        db.SaveChanges();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public bool ResetConfirms(Guid emailID)
        {
            try
            {
                using (var db = new GJobEntities())
                {
                    var isExisting = db.Emails.FirstOrDefault(x => x.ID == emailID);
                    if (isExisting != null)
                    {
                        isExisting.IsConfirmed = false;
                        var confirms = isExisting.EmailConfirmations.ToList();
                        if (confirms.Any())
                        {
                            db.EmailConfirmations.RemoveRange(confirms);
                        }

                        db.SaveChanges();

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }
    }
}