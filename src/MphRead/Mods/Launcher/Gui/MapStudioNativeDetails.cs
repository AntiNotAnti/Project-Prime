using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorObjects, INativeStudioEditorProperties
    {
        private NativeStudioObjectDelete? _objectDelete;
        public NativeStudioResult CreateObject(NativeStudioEditTicket ticket, NativeStudioCreateKind kind)
        {
            CheckThread(); _objectDelete = null;
            if (!Current(ticket) || !Enum.IsDefined(kind)) return Stale();
            try
            {
                MapStudioObjectCommands.Create(_document, (MapStudioObjectKind)(int)kind, _viewport.GetPlacementPoint());
                _viewport.FrameSelection(); return new(true,"OBJECT CREATED // EXISTING UNDO HISTORY RETAINED");
            }
            catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
            {return new(false,"OBJECT COULD NOT BE CREATED // CURRENT DOCUMENT RETAINED");}
        }
        public NativeStudioResult DuplicateObjects(NativeStudioEditTicket ticket)
        {
            CheckThread(); _objectDelete = null; if(!Current(ticket))return Stale();
            var added=MapStudioObjectCommands.DuplicateSelection(_document);
            if(added.Length==0)return new(false,"SELECT OBJECTS TO DUPLICATE");
            _viewport.FrameSelection();return new(true,"DUPLICATED SELECTION // NEW OBJECT IDENTITIES");
        }
        public NativeStudioObjectDelete? PrepareObjectDelete(NativeStudioEditTicket ticket)
        {
            CheckThread(); _objectDelete=null;if(!Current(ticket))return null;
            var rows=MapObjects.All(_document.Project.Definition).Where(o=>_document.Selection.Contains(o.Id)&&o.Value is not MapGeometry{Locked:true}).ToArray();
            if(rows.Length==0)return null;
            return _objectDelete=new(ticket,Guid.NewGuid(),rows.Length==1?rows[0].ToString():rows.Length+" SELECTED OBJECTS");
        }
        public NativeStudioResult ConfirmObjectDelete(NativeStudioObjectDelete confirmation)
        {
            CheckThread();var pending=_objectDelete;_objectDelete=null;
            if(pending==null||pending!=confirmation||!Current(confirmation.Owner))return Stale();
            return MapStudioObjectCommands.DeleteSelection(_document)?new(true,"SELECTED OBJECTS DELETED // UNDO RESTORES THEM"):new(false,"LOCKED OR EMPTY SELECTION RETAINED");
        }
        public void CancelObjectDelete(){CheckThread();if(Alive)_objectDelete=null;}
        public NativeStudioPropertySnapshot ReadProperties(NativeStudioEditTicket ticket)
        {
            CheckThread();
            if(!Current(ticket)||ticket.Selection==Guid.Empty)return new(new(ticket,""),Array.Empty<NativeStudioPropertyField>());
            var fields=MapObjectPropertySchema.Read(_document.Project.Definition,ticket.Selection);
            string schema=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",fields.Select(f=>f.Id+":"+f.Kind+":"+f.Min+":"+f.Max+":"+string.Join(",",f.Choices??Array.Empty<string>())))+"|materials:"+_document.Project.Definition.Materials.Count)));
            return new(new(ticket,schema),Array.AsReadOnly(fields.Select(f=>new NativeStudioPropertyField(f.Id,f.Label,(NativeStudioPropertyKind)(int)f.Kind,f.Value,f.MaxLength,f.Min,f.Max,f.Choices)).ToArray()));
        }
        public NativeStudioResult ApplyProperties(NativeStudioPropertyTicket ticket,IReadOnlyDictionary<string,string> patch)
        {
            CheckThread();_objectDelete=null;
            if(!Current(ticket.Owner)||ReadProperties(ticket.Owner).Ticket!=ticket)return Stale();
            try
            {
                MapObjectPropertySchema.Apply(_document,ticket.Owner.Selection,patch);
                return new(true,"PROPERTIES APPLIED // ONE EXISTING HISTORY TRANSACTION");
            }
            catch(Exception error) when(error is FormatException or ArgumentException or InvalidOperationException)
            {return new(false,error.Message.Length>256?error.Message[..256]:error.Message);}
        }
    }
}
