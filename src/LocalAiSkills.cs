using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FreeIsland
{
    [DataContract] internal sealed class LocalAiSkill
    {
        [DataMember(Name = "id", IsRequired = true)] public string Id { get; set; }
        [DataMember(Name = "name", IsRequired = true)] public string Name { get; set; }
        [DataMember(Name = "actions", IsRequired = true)] public List<LocalAiAction> Actions { get; set; }
    }

    [DataContract] internal sealed class LocalAiSkillDocument
    {
        [DataMember(Name = "version", IsRequired = true)] public int Version { get; set; }
        [DataMember(Name = "skills", IsRequired = true)] public List<LocalAiSkill> Skills { get; set; }
    }

    // A skill is a saved, validated proposal. This store never executes actions.
    internal sealed class LocalAiSkillStore
    {
        private const int MaximumSkills = 12, MaximumFileBytes = 65536;
        private readonly string directory, path;
        private readonly object gate = new object();
        private List<LocalAiSkill> skills = new List<LocalAiSkill>();
        private byte[] loadedBytes;
        internal string Error { get; private set; }

        internal LocalAiSkillStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("请选择技能保存目录。", "directory");
            this.directory = Path.GetFullPath(directory);
            path = Path.Combine(this.directory, "assistant-skills.json");
            try
            {
                loadedBytes = ReadExisting();
                if (loadedBytes == null) return;
                // Consume the complete JSON document, including its end, before accepting it.
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(loadedBytes, new System.Xml.XmlDictionaryReaderQuotas { MaxDepth = 16, MaxStringContentLength = 8192, MaxArrayLength = MaximumFileBytes }))
                    while (reader.Read()) { }
                LocalAiSkillDocument document;
                using (var stream = new MemoryStream(loadedBytes))
                    document = (LocalAiSkillDocument)new DataContractJsonSerializer(typeof(LocalAiSkillDocument)).ReadObject(stream);
                if (document == null || document.Version != 1 || document.Skills == null || document.Skills.Count > MaximumSkills)
                    throw new InvalidDataException("技能文件的版本或数量无效。");
                var validated = new List<LocalAiSkill>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var skill in document.Skills)
                {
                    if (skill == null || !ValidId(skill.Id) || !ids.Add(skill.Id)) throw new InvalidDataException("技能编号无效或重复。");
                    string name = ValidateName(skill.Name);
                    if (name != skill.Name) throw new InvalidDataException("技能名称格式无效。");
                    validated.Add(new LocalAiSkill { Id = skill.Id, Name = name, Actions = ValidateActions(skill.Actions) });
                }
                skills = validated;
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                Error = "技能文件无法读取，原文件已保留。请备份并修复文件后重新打开浮岛。";
            }
        }

        internal IList<LocalAiSkill> List()
        {
            lock (gate) return skills.Select(Clone).ToList().AsReadOnly();
        }

        internal LocalAiSkill Save(string name, IList<LocalAiAction> actions)
        {
            lock (gate)
            {
                EnsureWritable();
                if (skills.Count >= MaximumSkills) throw new InvalidOperationException("最多保存 12 个技能，请先删除不再使用的技能。");
                var skill = new LocalAiSkill { Id = Guid.NewGuid().ToString("D"), Name = ValidateName(name), Actions = ValidateActions(actions) };
                var updated = skills.Select(Clone).ToList();
                updated.Add(skill);
                Persist(updated);
                skills = updated;
                return Clone(skill);
            }
        }

        internal void Remove(string id)
        {
            lock (gate)
            {
                EnsureWritable();
                if (!ValidId(id)) throw new ArgumentException("技能编号无效。", "id");
                var updated = skills.Where(s => s.Id != id).Select(Clone).ToList();
                if (updated.Count == skills.Count) return;
                Persist(updated);
                skills = updated;
            }
        }

        private void EnsureWritable()
        {
            if (!string.IsNullOrEmpty(Error)) throw new InvalidOperationException(Error);
        }

        private byte[] ReadExisting()
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length == 0 || stream.Length > MaximumFileBytes) throw new InvalidDataException("技能文件大小无效。");
                    var bytes = new byte[(int)stream.Length];
                    int offset = 0, read;
                    while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                    if (offset != bytes.Length || stream.ReadByte() != -1) throw new InvalidDataException("技能文件读取不完整。");
                    return bytes;
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private void VerifyUnchanged()
        {
            byte[] current = ReadExisting();
            if (loadedBytes == null ? current != null : current == null || !loadedBytes.SequenceEqual(current))
                throw new InvalidDataException("技能文件已在外部更改，请重新打开浮岛后再保存。");
        }

        private void Persist(List<LocalAiSkill> updated)
        {
            string temporary = Path.Combine(directory, ".assistant-skills-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                VerifyUnchanged();
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(LocalAiSkillDocument)).WriteObject(stream, new LocalAiSkillDocument { Version = 1, Skills = updated });
                    if (stream.Length > MaximumFileBytes) throw new InvalidDataException("技能文件过大。");
                    bytes = stream.ToArray();
                }
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                VerifyUnchanged();
                // Same-directory replace is atomic on supported Windows filesystems.
                // If replacing is unsupported or denied, preserve the existing file and report the error.
                if (loadedBytes == null) File.Move(temporary, path);
                else File.Replace(temporary, path, null);
                loadedBytes = bytes;
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                Error = ex is InvalidDataException ? "技能文件已更改或格式无效，原文件已保留。请重新打开浮岛后再试。" : "无法保存技能，原文件已保留。请检查目录是否可写后重新打开浮岛。";
                throw new InvalidOperationException(Error, ex);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool IsStorageError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is SerializationException || ex is ArgumentException || ex is NotSupportedException || ex is System.Xml.XmlException;
        }

        private static bool ValidId(string id)
        {
            Guid value;
            return id != null && Guid.TryParseExact(id, "D", out value) && value != Guid.Empty && id == value.ToString("D");
        }

        private static string ValidateName(string name)
        {
            if (name == null || name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
                throw new ArgumentException("技能名称不能包含控制字符。", "name");
            name = name.Trim();
            if (name.Length < 1 || name.Length > 24) throw new ArgumentException("技能名称请使用 1 至 24 个字符。", "name");
            return name;
        }

        private static List<LocalAiAction> ValidateActions(IList<LocalAiAction> actions)
        {
            if (actions == null || actions.Count < 1 || actions.Count > 3) throw new ArgumentException("每个技能需要 1 至 3 个快捷操作。", "actions");
            if (actions.Any(a => a == null || a.Kind == null || a.Kind.Length > 24 || a.Title == null || a.Title.Length > 24))
                throw new InvalidDataException("技能的快捷操作格式无效。");
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(LocalAiProposal)).WriteObject(stream, new LocalAiProposal { Actions = actions.ToList() });
                return LocalAiActionRules.Parse(Encoding.UTF8.GetString(stream.ToArray())).ToList();
            }
        }

        private static LocalAiSkill Clone(LocalAiSkill skill)
        {
            return new LocalAiSkill { Id = skill.Id, Name = skill.Name, Actions = skill.Actions.Select(a => new LocalAiAction { Kind = a.Kind, Title = a.Title, Seconds = a.Seconds }).ToList() };
        }
    }
}
