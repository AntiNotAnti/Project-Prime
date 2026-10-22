using System.Reflection;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

/// <summary>Real native keyboard controls, paged property drafts, and the retained document on the caller's GL context.</summary>
internal static class StudioPropertyGlChecks
{
    internal static void Run(RmlUiHost host, RmlUiPageManager pages, NativeStudioEditorPage page,
        MapStudioScreen owner, int width, int height, Action<bool, string> Check)
    {
        var document = Field<MapDocument>(owner, "_document");
        var history = document.History;
        var viewport = Field<MapViewport>(owner, "_viewport");
        var session = Field<INativeStudioEditorSession>(page, "_session");
        var details = Field<NativeStudioEditorDetails>(page, "_details");
        var controller = Field<NativeStudioEditorController>(page, "_controller");
        object? renderer = Field<object?>(viewport, "_renderer");
        var created = new Dictionary<NativeStudioCreateKind, Guid>();
        string[] createControls = { "box", "wedge", "prism", "convex", "mesh", "spawn", "pickup", "pad", "link" };
        Type[] createTypes = { typeof(MapBox), typeof(MapWedge), typeof(MapPrism), typeof(MapConvexBrush), typeof(MapMesh),
            typeof(MapSpawn), typeof(MapItem), typeof(MapJumpPad), typeof(MapNavigationLink) };
        for (int i = 0; i < createControls.Length; i++)
        {
            var beforeIds = MapObjects.All(document.Project.Definition).Select(o => o.Id).ToHashSet();
            var beforeState = document.CurrentStateId;
            int beforeCount = history.CommandCount;
            Action("native_create_" + createControls[i], 60 + i);
            Guid id = document.ActiveObjectId;
            var added = MapObjects.All(document.Project.Definition).Where(o => !beforeIds.Contains(o.Id)).ToArray();
            Check(added.Length == 1 && id != Guid.Empty && added[0].Id == id && added[0].Value.GetType() == createTypes[i]
                && document.Selection.SetEquals(new[] { id }) && beforeState != document.CurrentStateId
                && history.CommandCount == beforeCount + 1,
                "actual native creation control adds and selects one typed object in the retained history: " + createControls[i]);
            created[(NativeStudioCreateKind)i] = id;
        }
        Check(created.Values.Distinct().Count() == 9, "all nine native creation controls assign distinct object identities");

        Guid originalLink = created[NativeStudioCreateKind.NavigationLink];
        string beforeDuplicate = document.Project.Definition.Serialize();
        Action("native_editor_duplicate", 69);
        Guid duplicate = document.ActiveObjectId;
        string afterDuplicate = document.Project.Definition.Serialize();
        Check(duplicate != originalLink && duplicate != Guid.Empty
            && MapObjects.Find(document.Project.Definition, originalLink) != null
            && MapObjects.Find(document.Project.Definition, duplicate)?.Value is MapNavigationLink,
            "actual Duplicate preserves the source and selects a new typed identity");
        Action("native_editor_undo", 33);
        Check(document.Project.Definition.Serialize() == beforeDuplicate, "native duplicate undo restores exact document bytes");
        Action("native_editor_redo", 34);
        Check(document.Project.Definition.Serialize() == afterDuplicate, "native duplicate redo restores exact document bytes");
        Select(duplicate);
        var beforeCancel = document.CurrentStateId;
        Action("native_editor_delete", 70);
        Check(details.Delete != null && host.FocusedElement() == "native_editor_confirm_delete",
            "newly visible native delete confirmation receives focus after layout");
        Action("native_editor_cancel_delete", 72);
        Check(details.Delete == null && host.FocusedElement() == "native_editor_delete"
            && document.CurrentStateId == beforeCancel && document.Project.Definition.Serialize() == afterDuplicate,
            "native Keep Objects retires confirmation, returns focus, and preserves document/history");
        Action("native_editor_delete", 70);
        Action("native_editor_confirm_delete", 71);
        string afterDelete = document.Project.Definition.Serialize();
        Check(MapObjects.Find(document.Project.Definition, duplicate) == null
            && MapObjects.Find(document.Project.Definition, originalLink) != null && details.Delete == null,
            "native delete confirmation removes only the reviewed duplicate");
        Action("native_editor_undo", 33);
        Check(document.Project.Definition.Serialize() == afterDuplicate, "native confirmed-delete undo restores exact object identity and document bytes");
        Action("native_editor_redo", 34);
        Check(document.Project.Definition.Serialize() == afterDelete, "native confirmed-delete redo restores exact document bytes");

        Guid meshId = created[NativeStudioCreateKind.Mesh];
        Select(meshId);
        string beforeProperties = document.Project.Definition.Serialize();
        var beforePropertiesState = document.CurrentStateId;
        int beforePropertyCommands = history.CommandCount;
        Draft("label", "GL typed mesh");
        Draft("size.x", "6");
        Draft("rotation.z", "45");
        Draft("shade", "0.5");
        Draft("uvOffset.y", "-0.25");
        Check(document.Project.Definition.Serialize() == beforeProperties && document.CurrentStateId == beforePropertiesState,
            "typed drafts survive native field paging without mutating the retained document");
        Action("native_property_apply", 75);
        string afterProperties = document.Project.Definition.Serialize();
        var mesh = (MapMesh)MapObjects.Find(document.Project.Definition, meshId)!.Value;
        Check(mesh.Label == "GL typed mesh" && mesh.Transform.Scale[0] == 6 && mesh.Shade == .5f && mesh.Uv.Offset[1] == -.25f
            && mesh.Transform.Rotation[2] != 0 && history.CommandCount == beforePropertyCommands + 1,
            "one native Apply commits text/numbers/rotation/UV drafts from multiple pages in one history command");
        Action("native_editor_undo", 33);
        Check(document.Project.Definition.Serialize() == beforeProperties, "native typed-property undo restores exact document bytes");
        Action("native_editor_redo", 34);
        Check(document.Project.Definition.Serialize() == afterProperties, "native typed-property redo restores exact document bytes");
        var noOpState = document.CurrentStateId;
        Action("native_property_apply", 75);
        Check(document.CurrentStateId == noOpState && document.Project.Definition.Serialize() == afterProperties,
            "unchanged native property Apply adds no history state or quaternion drift");

        Draft("label", "MUST NOT APPLY");
        Draft("position.x", "NaN");
        Action("native_property_apply", 75);
        Check(document.CurrentStateId == noOpState && document.Project.Definition.Serialize() == afterProperties,
            "native invalid numeric draft atomically rejects a simultaneous valid label draft");
        Action("native_property_reset", 76);
        Check(details.Rows.All(row => row.Value == row.Field.Value), "native Reset reloads the original typed values after invalid input");
        var materialField = MapObjectPropertySchema.Read(document.Project.Definition, meshId).Single(f => f.Id == "material");
        Draft("label", "MATERIAL MUST NOT APPLY");
        Draft("material", ((int)materialField.Max!.Value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Action("native_property_apply", 75);
        Check(document.CurrentStateId == noOpState && document.Project.Definition.Serialize() == afterProperties,
            "native material index uses global document bounds and rejects the whole invalid patch");
        Action("native_property_reset", 76);

        // Every cycle action is exercised on an actual visible Boolean slot, even across pages.
        string[] cycleFields = { "reflectBeams", "ignorePlayers", "ignoreBeams", "solid", "damaging", "hidden", "locked", "collisionOnly" };
        for (int slot = 0; slot < cycleFields.Length; slot++)
        {
            var location = Locate(cycleFields[slot]); PageTo(location.Page);
            int visibleSlot = location.Slot;
            Check(visibleSlot is >= 0 and < 8, "real mesh schema resolves a visible Boolean slot for " + cycleFields[slot]);
            string beforeDraft = details.Rows[visibleSlot].Value;
            Action("native_property_cycle" + visibleSlot, 77 + visibleSlot);
            Check(details.Rows[visibleSlot].Value != beforeDraft && document.Project.Definition.Serialize() == afterProperties,
                "actual native cycle intent changes only its frozen property draft: " + cycleFields[slot]);
        }
        Action("native_property_reset", 76);
        var terrain = Locate("terrain"); PageTo(terrain.Page);
        string priorTerrain = details.Rows[terrain.Slot].Value;
        Action("native_property_cycle" + terrain.Slot, 77 + terrain.Slot);
        string nextTerrain = details.Rows[terrain.Slot].Value;
        Check(nextTerrain != priorTerrain && details.Rows[terrain.Slot].Field.Choices!.Contains(nextTerrain),
            "actual native choice cycle stays within the known terrain choices");
        Action("native_property_apply", 75);
        Check(((MapMesh)MapObjects.Find(document.Project.Definition, meshId)!.Value).Terrain == nextTerrain,
            "native choice draft applies to the real retained typed object");

        string beforeExternal = document.Project.Definition.Serialize();
        Draft("label", "STALE MUST NOT APPLY");
        // A real history mutation simulates owner work occurring after a native draft was captured.
        document.EditObjects("Synthetic concurrent owner edit", new[] { meshId }, scratch =>
            ((MapGeometry)MapObjects.Find(scratch, meshId)!.Value).Label = "Owner changed after draft");
        string afterExternal = document.Project.Definition.Serialize();
        var externalState = document.CurrentStateId;
        Settle();
        Action("native_property_apply", 75);
        Check(document.CurrentStateId == externalState && document.Project.Definition.Serialize() == afterExternal
            && details.Status.Contains("CHANGED", StringComparison.Ordinal),
            "native frozen property ticket rejects stale drafts after real owner history changes");
        Action("native_editor_undo", 33);
        Check(document.Project.Definition.Serialize() == beforeExternal, "stale-draft fixture undo restores the exact pre-owner-edit document");
        Check(ReferenceEquals(document, Field<MapDocument>(owner, "_document")) && ReferenceEquals(history, document.History)
            && ReferenceEquals(viewport, Field<MapViewport>(owner, "_viewport"))
            && ReferenceEquals(renderer, Field<object?>(viewport, "_renderer")) && session.Capture().Alive && !page.Closed,
            "all native object/property controls retain the same document, history, viewport renderer, and live editor lease");
        Check(GL.GetError() == ErrorCode.NoError, "native object/property frames leave no GL error on the existing context");

        void Frame()
        {
            page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.UseProgram(0); GL.ActiveTexture(TextureUnit.Texture0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.02f, .04f, .06f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Check(page.DrawViewport(width, height, width, height), "property-control frame draws through the existing retained viewport");
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle() { for (int i = 0; i < 3; i++) Frame(); }
        void Action(string id, int argument)
        {
            host.DiscardIntents();
            Check(host.FocusDocument(page.Document, id), "native property/object control focuses: " + id); Frame();
            host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
            Check(host.TryTakeIntent(out var intent) && intent.Document == page.Document
                && intent.Kind == RmlUiIntentKind.StudioAction && intent.Argument == argument
                && page.Handle(intent) && !host.TryTakeIntent(out _), "one exact native property/object keyboard intent: " + id);
            Settle();
        }
        void Select(Guid id)
        {
            var rows = session.Capture().Rows;
            int index = rows.ToList().FindIndex(row => row.Id == id);
            Check(index >= 0, "created object is present in the real native hierarchy");
            int targetPage = index / NativeStudioEditorController.PageSize;
            while (controller.Current().Page < targetPage) Action("native_editor_next", 46);
            while (controller.Current().Page > targetPage) Action("native_editor_previous", 45);
            int slot = index % NativeStudioEditorController.PageSize;
            Action("native_editor_row" + slot, 47 + slot);
            Check(document.ActiveObjectId == id, "paged native hierarchy selects the requested real object identity");
        }
        (int Page, int Slot) Locate(string fieldId)
        {
            var fields = MapObjectPropertySchema.Read(document.Project.Definition, document.ActiveObjectId);
            int index = fields.ToList().FindIndex(field => field.Id == fieldId);
            Check(index >= 0, "real typed schema exposes property " + fieldId);
            return (index / 8, index % 8);
        }
        void PageTo(int target)
        {
            while (details.PageIndex < target) Action("native_property_next", 74);
            while (details.PageIndex > target) Action("native_property_previous", 73);
        }
        void Draft(string fieldId, string value)
        {
            var location = Locate(fieldId); PageTo(location.Page);
            Check(host.FocusDocument(page.Document, "native_property_value" + location.Slot), "native typed input focuses: " + fieldId);
            host.SetField(page.Document, "native_property_value" + location.Slot, value);
        }
    }

    private static T Field<T>(object value, string name)
        => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
