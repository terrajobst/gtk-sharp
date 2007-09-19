// GtkSharp.Generation.InterfaceGen.cs - The Interface Generatable.
//
// Author: Mike Kestner <mkestner@speakeasy.net>
//
// Copyright (c) 2001-2003 Mike Kestner
// Copyright (c) 2004, 2007 Novell, Inc.
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of version 2 of the GNU General Public
// License as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public
// License along with this program; if not, write to the
// Free Software Foundation, Inc., 59 Temple Place - Suite 330,
// Boston, MA 02111-1307, USA.


namespace GtkSharp.Generation {

	using System;
	using System.Collections;
	using System.IO;
	using System.Xml;

	public class InterfaceGen : ObjectBase {

		ArrayList vms = new ArrayList ();
		ArrayList members = new ArrayList ();

		public InterfaceGen (XmlElement ns, XmlElement elem) : base (ns, elem) 
		{
			foreach (XmlNode node in elem.ChildNodes) {
				switch (node.Name) {
				case "virtual_method":
					VirtualMethod vm = new VirtualMethod (node as XmlElement, this);
					vms.Add (vm);
					members.Add (vm);
					break;
				case "signal":
					members.Add ((node as XmlElement).GetAttribute ("cname").Replace ('-', '_'));
					break;
				default:
					if (!IsNodeNameHandled (node.Name))
						Console.WriteLine ("Unexpected node " + node.Name + " in " + CName);
					break;
				}
			}
		}

		public override bool ValidateForSubclass ()
		{
			ArrayList invalids = new ArrayList ();

			foreach (Method method in methods.Values) {
				if (!method.Validate ()) {
					Console.WriteLine ("in type " + QualifiedName);
					invalids.Add (method);
				}
			}
			foreach (Method method in invalids)
				methods.Remove (method.Name);
			invalids.Clear ();

			return base.ValidateForSubclass ();
		}

		string IfaceName {
			get {
				return Name + "Iface";
			}
		}

		void GenerateIfaceStruct (StreamWriter sw)
		{
			sw.WriteLine ("\t\tstatic " + IfaceName + " iface;");
			sw.WriteLine ();
			sw.WriteLine ("\t\tstruct " + IfaceName + " {");
			sw.WriteLine ("\t\t\tpublic IntPtr gtype;");
			sw.WriteLine ("\t\t\tpublic IntPtr itype;");
			sw.WriteLine ();

			foreach (object member in members) {
				if (member is System.String)
					sw.WriteLine ("\t\t\tpublic IntPtr " + member + ";");
				else if (member is VirtualMethod) {
					VirtualMethod vm = member as VirtualMethod;
					bool has_method = methods [vm.Name] != null;
					if (!has_method)
						Console.WriteLine ("Interface " + QualifiedName + " virtual method " + vm.Name + " has no matching method to invoke.");
					string type = has_method && vm.IsValid ? vm.Name + "Delegate" : "IntPtr";
					sw.WriteLine ("\t\t\tpublic " + type + " " + vm.CName + ";");
				}
			}

			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenerateStaticCtor (StreamWriter sw)
		{
			sw.WriteLine ("\t\tstatic " + Name + "Adapter ()");
			sw.WriteLine ("\t\t{");
			foreach (VirtualMethod vm in vms) {
				bool has_method = methods [vm.Name] != null;
				if (has_method && vm.IsValid)
					sw.WriteLine ("\t\t\tiface.{0} = new {1}Delegate ({1}Callback);", vm.CName, vm.Name);
			}
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenerateInitialize (StreamWriter sw)
		{
			sw.WriteLine ("\t\tstatic void Initialize (IntPtr ifaceptr, IntPtr data)");
			sw.WriteLine ("\t\t{");
			sw.WriteLine ("\t\t\t" + IfaceName + " native_iface = (" + IfaceName + ") Marshal.PtrToStructure (ifaceptr, typeof (" + IfaceName + "));");
			foreach (VirtualMethod vm in vms)
				sw.WriteLine ("\t\t\tnative_iface." + vm.CName + " = iface." + vm.CName + ";");
			sw.WriteLine ("\t\t\tMarshal.StructureToPtr (native_iface, ifaceptr, false);");
			sw.WriteLine ("\t\t\tGCHandle gch = (GCHandle) data;");
			sw.WriteLine ("\t\t\tgch.Free ();");
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenerateCallbacks (StreamWriter sw)
		{
			foreach (VirtualMethod vm in vms) {
				if (methods [vm.Name] == null)
					continue;
				sw.WriteLine ();
				vm.GenerateCallback (sw);
			}
		}

		void GenerateCtor (StreamWriter sw)
		{
			sw.WriteLine ("\t\tpublic " + Name + "Adapter ()");
			sw.WriteLine ("\t\t{");
			sw.WriteLine ("\t\t\tInitHandler = new GLib.GInterfaceInitHandler (Initialize);");
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenerateGType (StreamWriter sw)
		{
			Method m = GetMethod ("GetType");
			m.GenerateImport (sw);
			sw.WriteLine ("\t\tpublic override GLib.GType GType {");
			sw.WriteLine ("\t\t\tget {");
			sw.WriteLine ("\t\t\t\treturn new GLib.GType (" + m.CName +  " ());");
			sw.WriteLine ("\t\t\t}");
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenerateAdapter (GenerationInfo gen_info)
		{
			StreamWriter sw = gen_info.Writer = gen_info.OpenStream (Name + "Adapter");

			sw.WriteLine ("namespace " + NS + " {");
			sw.WriteLine ();
			sw.WriteLine ("\tusing System;");
			sw.WriteLine ("\tusing System.Runtime.InteropServices;");
			sw.WriteLine ();
			sw.WriteLine ("#region Autogenerated code");
			sw.WriteLine ("\tinternal class " + Name + "Adapter : GLib.GInterfaceAdapter {");
			sw.WriteLine ();

			GenerateIfaceStruct (sw);
			GenerateStaticCtor (sw);
			GenerateCallbacks (sw);
			GenerateInitialize (sw);
			GenerateCtor (sw);
			GenerateGType (sw);

			sw.WriteLine ("\t}");
			sw.WriteLine ("#endregion");
			sw.WriteLine ("}");
			sw.Close ();
			gen_info.Writer = null;
		}

		void GenSignals (GenerationInfo gen_info)
		{
			if (sigs.Count == 0)
				return;

			StreamWriter sw = gen_info.Writer;

			sw.WriteLine ();
			sw.WriteLine ("\t\t// signals");
			foreach (Signal sig in sigs.Values) {
				sig.GenerateDecl (sw);
				sig.GenEventHandler (gen_info);
			}
		}

		Hashtable GenVMDecls (StreamWriter sw)
		{
			if (vms.Count == 0)
				return new Hashtable ();

			sw.WriteLine ();
			sw.WriteLine ("\t\t// virtual methods");
			Hashtable vm_decls = new Hashtable ();
			foreach (VirtualMethod vm in vms) {
				sw.WriteLine ("\t\t" + vm.Declaration);
				vm_decls [vm.Declaration] = vm;
			}
			return vm_decls;
		}

		void GenMethodDecls (StreamWriter sw, Hashtable vm_decls)
		{
			if (methods.Count == 0)
				return;

			bool need_comment = true;
			foreach (Method method in methods.Values) {
				//if (IgnoreMethod (method))
					//continue;

				if (!vm_decls.Contains (method.Declaration) && method.Name != "GetGType") {
					if (need_comment) {
						sw.WriteLine ();
						sw.WriteLine ("\t\t// non-virtual methods");
						need_comment = false;
					}
					method.GenerateDecl (sw);
				}
			}
		}

		public override void Generate (GenerationInfo gen_info)
		{
			GenerateAdapter (gen_info);
			StreamWriter sw = gen_info.Writer = gen_info.OpenStream (Name);

			sw.WriteLine ("namespace " + NS + " {");
			sw.WriteLine ();
			sw.WriteLine ("\tusing System;");
			sw.WriteLine ();
			sw.WriteLine ("#region Autogenerated code");
			sw.WriteLine ("\t[GLib.GInterface (typeof (" + Name + "Adapter))]");
			sw.WriteLine ("\tpublic interface " + Name + " : GLib.IWrapper {");
			sw.WriteLine ();
			
			foreach (Signal sig in sigs.Values) {
				sig.GenerateDecl (sw);
				sig.GenEventHandler (gen_info);
			}

			foreach (Method method in methods.Values) {
				if (IgnoreMethod (method, this))
					continue;
				method.GenerateDecl (sw);
			}

			foreach (Property prop in props.Values)
				prop.GenerateDecl (sw, "\t\t");

			AppendCustom (sw, gen_info.CustomDir);

			sw.WriteLine ("\t}");
			sw.WriteLine ("#endregion");
			sw.WriteLine ("}");
			sw.Close ();
			gen_info.Writer = null;
			Statistics.IFaceCount++;
		}
	}
}

