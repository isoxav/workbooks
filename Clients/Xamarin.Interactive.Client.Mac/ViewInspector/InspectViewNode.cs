//
// Author:
//   Aaron Bockover <abock@xamarin.com>
//
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;

using AppKit;
using CoreGraphics;
using Foundation;
using SceneKit;

using Xamarin.Interactive.Client.ViewInspector;
using Xamarin.Interactive.Remote;

namespace Xamarin.Interactive.Client.Mac.ViewInspector
{
    sealed class InspectViewNode : SCNNode, IInspectTree3DNode<InspectViewNode>
    {
        const float ZSpacing = 20f;
        const float ZFightDenominator = 20f;

        public InspectTreeNode Node { get; }
        public InspectView View => Node?.View;

        NSColor boundsColor;
        int childIndex;

        public InspectViewNode (InspectTreeNode node, InspectTreeState state)
        {
            Node = node;
            node.PropertyChanged += NodePropertyChanged;
            this.childIndex = state.AddChild (node.View);
        }

        void NodePropertyChanged (object sender, PropertyChangedEventArgs args)
        {
            InspectTreeNode senderNode = sender as InspectTreeNode;
            switch (args.PropertyName) {
            case nameof (Node.IsSelected):
            case nameof (Node.IsMouseOver):
            case nameof (Node.IsExpanded):
                UpdateState ();
                break;
            }
        }

        static internal SCNGeometrySource [] CreateGeometrySources (nfloat width, nfloat height)
        {
            return new [] {
                SCNGeometrySource.FromVertices(new [] {
                    new SCNVector3 (0, 0, 0),
                    new SCNVector3 (0, height, 0),
                    new SCNVector3 (width, height, 0),
                    new SCNVector3 (width, 0, 0)
                }),
                SCNGeometrySource.FromNormals (new [] {
                    new SCNVector3 (0, 0, 1),
                    new SCNVector3 (0, 0, 1),
                    new SCNVector3 (0, 0, 1),
                    new SCNVector3 (0, 0, 1)
                }),
                SCNGeometrySource.FromTextureCoordinates (new [] {
                    new CGPoint (0,0),
                    new CGPoint (0,1),
                    new CGPoint (1,1),
                    new CGPoint (1,0)
                })
            };
        }

        static internal SCNGeometryElement CreateLines (nfloat width, nfloat height)
        {
            var data = NSData.FromArray (new byte [] { 0, 1, 1, 2, 2, 3, 3, 0 });
            return SCNGeometryElement.FromData (data, SCNGeometryPrimitiveType.Line, 4, 1);
        }

        static SCNGeometryElement CreatePlane (nfloat width, nfloat height)
        {
            var data = NSData.FromArray (new byte [] { 0, 1, 2, 0, 2, 3 });
            return SCNGeometryElement.FromData (data, SCNGeometryPrimitiveType.Triangles, 2, 1);
        }

        static SCNGeometry CreateGeometry (nfloat width, nfloat height)
            => SCNGeometry.Create (
                CreateGeometrySources (width, height),
                new [] {
                    CreatePlane (width, height),
                    CreatePlane (width, height),
                    CreateLines (width, height)
                });


        public void UpdateState ()
        {
            if (Node.IsSelected) {
                Geometry.Materials [0].Transparency = Node.IsMouseOver ? 0.75f : 0.5f;
                Geometry.Materials [0].Diffuse.ContentColor = NSColor.AlternateSelectedControl;
                Geometry.Materials [2].Transparency = 1f;
                Geometry.Materials [2].Diffuse.ContentColor = NSColor.AlternateSelectedControl;
            } else if (Node.IsMouseOver) {
                Geometry.Materials [0].Transparency = 0.5f;
                Geometry.Materials [0].Diffuse.ContentColor = NSColor.SelectedControl;
                Geometry.Materials [2].Transparency = 0.5f;
                Geometry.Materials [2].Diffuse.ContentColor = NSColor.AlternateSelectedControl;
            } else {
                Geometry.Materials [0].Transparency = 0;
                Geometry.Materials [2].Transparency = 1;
                Geometry.Materials [2].Diffuse.ContentColor = boundsColor;
            }
            foreach (var child in ChildNodes) {
                child.Hidden = !Node.IsExpanded;
            }
        }

        void BuildPrimaryPlane (InspectTreeState state)
        {
            var view = View;
            if (View.Layer != null)
                view = View.Layer;

            Geometry = CreateGeometry ((nfloat)view.Width, (nfloat)view.Height);

            var renderImage = state.Mode == DisplayMode.Content || state.Mode == DisplayMode.FramesAndContent;
            var materialContents = InspectViewMaterial.Create (view, renderImage);

            var firstMaterial = SCNMaterial.Create ();
            firstMaterial.Transparency = renderImage ? 1 : 0;
            firstMaterial.Diffuse.Contents = materialContents;
            firstMaterial.DoubleSided = true;

            var highlightMaterial = SCNMaterial.Create ();
            highlightMaterial.Diffuse.ContentColor = NSColor.SelectedMenuItem;
            highlightMaterial.Transparency = 0;
            highlightMaterial.DoubleSided = true;

            var renderBounds = state.Mode == DisplayMode.Frames || state.Mode == DisplayMode.FramesAndContent;
            var boundsMaterial = SCNMaterial.Create ();
            boundsMaterial.Diffuse.Contents = boundsColor = renderBounds ? NSColor.LightGray : NSColor.Clear;
            boundsMaterial.Transparency = 1;
            boundsMaterial.DoubleSided = true;

            Geometry.Materials = new [] { highlightMaterial, firstMaterial, boundsMaterial };
            UpdateState ();

            // This is a hack to avoid Z-fighting of planes/frames that intersect within
            // the same node-depth level (or if we ever allow ZSpacing to drop to zero).
            // z-fighting appears on some controls such as some subviews of UISwitch.
            // A better solution would be to only apply this where an intersection exists.
            var zFightOffset = childIndex / ZFightDenominator;
            var zOffset = ZSpacing + zFightOffset;
            //var siblingOffset = childIndex - (ParentNode as InspectViewNode)?.childIndex ?? 0;
            //var zOffset = siblingOffset * ZSpacing;

            if ((View.Parent == null && !View.IsFakeRoot)
                || (View.Parent?.IsFakeRoot ?? false)) {

                var unitScale = (nfloat)(1.0 / Math.Max (view.Width, view.Height));

                var translate = SCNMatrix4.CreateTranslation (
                    (nfloat)(-view.Width / 2.0),
                    (nfloat)(-view.Height / 2.0),
                    zOffset);

                var scale = SCNMatrix4.Scale (unitScale, -unitScale, unitScale);
                Transform = SCNMatrix4.Mult (translate, scale);
            } else if (!view.IsFakeRoot) {
                if (view.Transform != null) {
                    var parentTransform = SCNMatrix4.Identity;
                    var render = view.Transform;
                    var transform = new SCNMatrix4 {
                        M11 = (nfloat)render.M11,
                        M12 = (nfloat)render.M12,
                        M13 = (nfloat)render.M13,
                        M14 = (nfloat)render.M14,
                        M21 = (nfloat)render.M21,
                        M22 = (nfloat)render.M22,
                        M23 = (nfloat)render.M23,
                        M24 = (nfloat)render.M24,
                        M31 = (nfloat)render.M31,
                        M32 = (nfloat)render.M32,
                        M33 = (nfloat)render.M33,
                        M34 = (nfloat)render.M34,
                        M41 = (nfloat)render.OffsetX,
                        M42 = (nfloat)render.OffsetY,
                        M43 = (nfloat)render.OffsetZ,
                        M44 = (nfloat)render.M44
                    };
                    transform = SCNMatrix4.Mult (parentTransform, transform);
                    Transform = SCNMatrix4.Mult (
                        transform,
                        SCNMatrix4.CreateTranslation (0, 0, zOffset));

                } else {
                    Transform = SCNMatrix4.CreateTranslation (
                        (nfloat)view.X,
                        (nfloat)view.Y,
                        zOffset);
                }
            }
        }

        void IInspectTree3DNode<InspectViewNode>.BuildPrimaryPlane (InspectTreeState state)
            => BuildPrimaryPlane (state);
        
        void IInspectTree3DNode<InspectViewNode>.Add (InspectViewNode child)
            => AddChildNode (child);

        InspectViewNode IInspectTree3DNode<InspectViewNode>.BuildChild (InspectTreeNode node, InspectTreeState state)
        {
            var child = new InspectViewNode (node, state);
            node.Build3D (child, state);
            return child;
        }
    }
}
