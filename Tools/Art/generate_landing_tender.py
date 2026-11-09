import bpy
import math
import os
from mathutils import Vector

# Reproducible Blender-authored landing tender mesh. Local bounds are exactly one
# unit on X/Y/Z and become 90 x 120 x 500 metres in BuildingVisuals.Create.
bpy.ops.wm.read_factory_settings(use_empty=True)

MAT_NAMES = [
    "Tender primary armour", "Tender engine machinery", "Tender oxblood plating",
    "Tender aged brass", "Tender amber signal", "Tender cyan exhaust",
    "Tender recessed iron"
]
BASE_COLORS = [(0.16,0.17,0.18,1),(0.20,0.18,0.16,1),(0.27,0.035,0.025,1),
               (0.32,0.22,0.09,1),(0.85,0.33,0.06,1),(0.01,0.63,0.72,1),(0.06,0.07,0.075,1)]
mats=[]
for i,name in enumerate(MAT_NAMES):
    m=bpy.data.materials.new(name); m.diffuse_color=BASE_COLORS[i]; m.use_nodes=True
    bs=m.node_tree.nodes.get("Principled BSDF")
    bs.inputs["Base Color"].default_value=BASE_COLORS[i]
    bs.inputs["Metallic"].default_value=0.82 if i not in (4,5) else 0.25
    bs.inputs["Roughness"].default_value=0.3 if i not in (4,5) else 0.2
    if i==5:
        bs.inputs["Emission Color"].default_value=BASE_COLORS[i]
        bs.inputs["Emission Strength"].default_value=3.0
    mats.append(m)
parts=[]

def finish(obj, name, mat, bevel=0.0, smooth=False):
    obj.name=name
    obj.data.materials.clear(); obj.data.materials.append(mats[mat])
    if bevel>0:
        mod=obj.modifiers.new("machined edge radii", "BEVEL"); mod.width=bevel; mod.segments=3
        bpy.context.view_layer.objects.active=obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        normal=obj.modifiers.new("weighted surface normals", "WEIGHTED_NORMAL")
        normal.keep_sharp=True; normal.weight=50
        bpy.ops.object.modifier_apply(modifier=normal.name)
    if smooth:
        for p in obj.data.polygons: p.use_smooth=True
    parts.append(obj)
    return obj

def box(name, loc, dims, mat=0, bevel=0.008):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o,name,mat,min(bevel,min(dims)*0.44))

def cyl(name, loc, radius, depth, mat=1, vertices=32, axis=None, bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    o=bpy.context.object
    if axis is not None: o.rotation_mode='QUATERNION'; o.rotation_quaternion=Vector((0,0,1)).rotation_difference(Vector(axis).normalized())
    return finish(o,name,mat,bevel,True)

def sphere(name, loc, scale, mat=1):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, location=loc)
    o=bpy.context.object; o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,0,True)

def beam(name, a, b, radius, mat=1):
    a=Vector(a); b=Vector(b); d=b-a
    return cyl(name,(a+b)*0.5,radius,d.length,mat,16,d,0.002)

def loft_hull():
    sections=[(-.50,.025,.045),(-.445,.12,.12),(-.34,.25,.18),(-.18,.36,.22),(.06,.37,.22),(.27,.31,.19),(.42,.24,.15),(.50,.16,.10)]
    # Chamfered naval hull section; broad upper deck and a deep keel create a
    # tapered wedge rather than the previous stacked-box silhouette.
    ring=[(-.78,-.50),(.78,-.50),(1,-.24),(1,.22),(.78,.50),(-.78,.50),(-1,.22),(-1,-.24)]
    verts=[]
    for z,w,h in sections:
        for x,y in ring: verts.append((x*w,y*h,z))
    faces=[]
    faces.append(tuple(reversed(range(8))))
    for j in range(len(sections)-1):
        for k in range(8):
            a=j*8+k; b=j*8+(k+1)%8; c=(j+1)*8+(k+1)%8; d=(j+1)*8+k
            faces.append((a,b,c,d))
    faces.append(tuple((len(sections)-1)*8+k for k in range(8)))
    mesh=bpy.data.meshes.new("Tender tapered pressure hull mesh"); mesh.from_pydata(verts,[],faces); mesh.materials.append(mats[0]); mesh.update()
    obj=bpy.data.objects.new("Tender tapered pressure hull",mesh); bpy.context.collection.objects.link(obj); parts.append(obj)
    for p in mesh.polygons: p.use_smooth=False

loft_hull()

# Keel, armored dorsal spine and modular side armor.
box("Keel spine",(0,-.13,.0),(.23,.16,.78),6,.025)
box("Dorsal armored spine",(0,.105,.02),(.25,.065,.74),0,.025)
for i in range(12):
    z=-.332+i*.060
    width=.285-.025*abs(i-5.5)/5.5
    box("Individually bevelled dorsal armor tile",(0,.148,z),(width,.025,.052),0,.009)
    for side in (-1,1): sphere("Dorsal tile captive bolt",(side*(width*.42),.162,z-.018),(.0035,.0035,.0035),3)
# Break up the long exposed deck into unevenly sized, individually raised panels.
# The contrasting inset ribs and fasteners keep the surface legible at the game's
# 500 m scale without relying on flat color bands.
for side in (-1,1):
    for index,z in enumerate((-.31,-.235,-.155,-.075,.012,.101,.19,.275,.35)):
        panel_width=(.092,.108,.084,.118,.096)[index%5]
        panel_depth=(.062,.074,.058)[(index+side)%3]
        material=6 if index%4==3 else (2 if index%5==1 else 0)
        box("Raised dorsal service armor panel",(side*.155,.109,z),(panel_width,.014,panel_depth),material,.006)
        box("Inset deck panel seam",(side*.155,.117,z),(panel_width*.72,.004,panel_depth*.48),6,.002)
        box("Deck panel brass index rib",(side*.155,.120,z-panel_depth*.25),(panel_width*.62,.003,.0035),3,.001)
        for bolt_side in (-1,1):
            sphere("Dorsal service panel fastener",(side*.155+bolt_side*panel_width*.39,.119,z+panel_depth*.33),(.0035,.0035,.0035),3)
for side in (-1,1):
    x=side
    # Long layered sponsons, flush service galleries and bevelled armor tiles.
    box("Lift sponson root",(x*.27,-.005,.08),(.26,.20,.68),1,.035)
    box("Sponson top armor",(x*.28,.105,.08),(.25,.045,.63),0,.018)
    box("Sponson outer oxblood stripe",(x*.405,.025,-.015),(.012,.075,.39),2,.006)
    for z in (-.34,-.24,-.14,-.04,.06,.16,.26,.35):
        box("Sponson segmented plate",(x*.405,.02,z),(.018,.09,.075),0,.006)
        for y in (-.01,.035):
            sphere("Armor fastener",(x*.418,y,z-.025),(.004,.004,.004),3)
    # Recessed service channels with individual louvres.
    box("Engine service trench",(x*.405,-.045,.20),(.019,.025,.22),6,.004)
    for k in range(9):
        z=.105+k*.024
        box("Service trench louvre",(x*.418,-.043,z),(.008,.018,.010),3,.003)
    # Broad rounded engine nacelle. Three bell mouths per wing, in two banks.
    for row,y in enumerate((-.075,.075)):
        for col,z in enumerate((.315,.39,.465)):
            cx=x*.335
            cyl("Engine bell armored casing",(cx,y,z),.045,.072,1,40,None,.004)
            cyl("Bell lip brass ring",(cx,y,z+.039),.040,.012,3,40,None,.002)
            cyl("Exhaust shadow baffle",(cx,y,z+.047),.032,.009,6,40,None,.001)
            cyl("Cyan ion throat",(cx,y,z+.052),.025,.010,5,40,None,.001)
            cyl("Inner reactor aperture",(cx,y,z+.058),.012,.004,6,32,None,0)
            for t in range(12):
                a=2*math.pi*t/12
                sphere("Bell retaining bolt",(cx+.041*math.cos(a),y+.041*math.sin(a),z+.040),(.003,.003,.003),3)
    # Articulated landing assemblies with nested hydraulic struts and armor shells.
    for z in (-.27,.26):
        foot=(x*.43,-.488,z+(.015 if z<0 else -.015))
        knee=(x*.32,-.18,z)
        hip=(x*.25,-.035,z)
        beam("Upper landing-leg titanium ram",hip,knee,.022,1)
        beam("Landing leg oxblood fairing",(x*.27,-.06,z),(x*.35,-.22,z),.032,2)
        sphere("Landing knee knuckle",knee,(.040,.040,.040),3)
        beam("Lower landing-leg piston",knee,foot,.014,3)
        beam("Outer landing-leg fork",(x*.35,-.22,z),(x*.43,-.45,z),.018,1)
        box("Landing foot gimbal",(x*.43,-.451,z),(.095,.04,.105),6,.012)
        box("Broad magnetic landing pad",(x*.43,-.482,z),(.16,.018,.17),0,.007)
        box("Landing-pad contact face",(x*.43,-.493,z),(.12,.008,.12),6,.003)
        for stripe in (-1,1):
            box("Landing pad brass wear strip",(x*.43+stripe*.057,-.489,z),(.006,.005,.105),3,.002)
        for i in (-1,1):
            beam("Landing pad cleat",(x*.43+i*.05,-.49,z-.07),(x*.43+i*.05,-.49,z+.07),.006,1)

# Raised flight deck, cathedral-backed bridge, viewport glazing and antennas.
box("Flight deck lower armored block",(0,.19,-.06),(.27,.13,.28),2,.022)
box("Command bridge forward bevel",(0,.25,-.145),(.22,.10,.13),2,.026)
box("Bridge armored canopy",(0,.305,-.11),(.20,.035,.10),0,.012)
for side in (-1,1):
    for row in range(3):
        box("Bridge viewport",(side*(.046+row*.035),.282,-.178),(.020,.023,.008),5,.004)
        box("Viewport armored brow",(side*(.046+row*.035),.298,-.182),(.027,.007,.009),3,.002)
    # Gothic ribs, metallic buttresses and warm service lights along the bridge.
    for z in (-.13,-.07,-.01,.05):
        beam("Bridge cathedral rib",(side*.11,.20,z),(side*.13,.32,z+.015),.008,3)
    for z in (-.12,-.04,.04,.12):
        box("Bridge amber running lamp",(side*.19,.11,z),(.012,.012,.022),4,.003)
box("Command spire base",(0,.34,.00),(.12,.11,.13),2,.022)
box("Command spire crown",(0,.402,.01),(.085,.035,.095),3,.012)
for side in (-1,1):
    beam("Comms mast",(side*.035,.415,.02),(side*.055,.48,.05),.003,3)
    sphere("Mast signal beacon",(side*.055,.48,.05),(.008,.008,.008),4)
# Dorsal sensor arrays and armored communications housings.
for i in range(5):
    z=-.32+i*.10
    box("Dorsal sensor housing",(0,.165,z),(.12,.035,.055),6,.007)
    box("Sensor glass",(0,.185,z-.006),(.075,.008,.024),5,.003)
    for side in (-1,1):
        box("Aft deck armored module",(side*.13,.14,z),(.08,.045,.062),0,.009)

# Recessed docking collar and layered bow impact shield.
box("Bow ram buried collar",(0,.015,-.435),(.20,.14,.11),6,.028)
box("Bow heat shield",(0,.00,-.475),(.29,.18,.040),0,.035)
for side in (-1,1):
    for i in range(5):
        z=-.45+i*.018
        box("Heat shield ceramic vane",(side*(.06+i*.006),.0,z),(.012,.135,.006),3,.002)
# Side running lights, access ports, hatches, armor rails and repeated greeble detail.
for side in (-1,1):
    for i in range(16):
        z=-.36+i*.047
        box("Flush maintenance hatch",(side*.375,-.035,z),(.012,.045,.032),6,.005)
        box("Hatch release",(side*.383,-.035,z-.012),(.006,.009,.004),3,.001)
        if i%2==0:
            box("Lateral navigation light",(side*.386,.065,z),(.006,.010,.020),4,.002)
    beam("Upper hull brass reveal",(side*.20,.12,-.38),(side*.28,.12,.37),.004,3)
    beam("Lower hull protective rail",(side*.33,-.12,-.28),(side*.34,-.12,.28),.005,6)
    # Aft maneuvering thrusters point outward and aft for orbital alignment.
    for z in (-.31,-.20,.19,.30):
        axis=(side*.65,.08,.75)
        cyl("Attitude thruster housing",(side*.35,.0,z),.024,.046,1,24,axis,.003)
        cyl("Attitude thruster bronze lip",(side*.35+side*.017,.002,z+.012),.019,.008,3,24,axis,.002)
        cyl("Attitude thruster dark recess",(side*.35+side*.021,.003,z+.016),.012,.006,6,20,axis,.001)

# Merge source components directly so Unity receives one mesh with exactly seven
# deterministic material groups. Object transforms are baked into each vertex.
merged_vertices=[]; merged_faces=[]; merged_material_indices=[]
for obj in parts:
    base=len(merged_vertices); matrix=obj.matrix_world.copy()
    merged_vertices.extend([matrix @ v.co for v in obj.data.vertices])
    source_mats=[m.name if m else MAT_NAMES[0] for m in obj.data.materials]
    for poly in obj.data.polygons:
        merged_faces.append(tuple(base+i for i in poly.vertices))
        material_name=source_mats[poly.material_index] if poly.material_index<len(source_mats) else MAT_NAMES[0]
        merged_material_indices.append(next((i for i,n in enumerate(MAT_NAMES) if n==material_name),0))
mesh=bpy.data.meshes.new("Tender authored high detail mesh")
mesh.from_pydata(merged_vertices,[],merged_faces)
for material in mats: mesh.materials.append(material)
mesh.update()
for polygon,material_index in zip(mesh.polygons,merged_material_indices): polygon.material_index=material_index
for obj in list(parts): bpy.data.objects.remove(obj,do_unlink=True)
ship=bpy.data.objects.new("TenderHull",mesh); bpy.context.collection.objects.link(ship)
# Normalize the modeled bounds per axis so the in-game world scale is exactly 90 x 120 x 500m.
bounds=[v.co.copy() for v in ship.data.vertices]
minimum=Vector((min(v.x for v in bounds),min(v.y for v in bounds),min(v.z for v in bounds)))
maximum=Vector((max(v.x for v in bounds),max(v.y for v in bounds),max(v.z for v in bounds)))
center=(minimum+maximum)*.5; extent=maximum-minimum
for v in ship.data.vertices:
    v.co=Vector(((v.co.x-center.x)/extent.x,(v.co.y-center.y)/extent.y,(v.co.z-center.z)/extent.z))
ship.data.update()
# leave object scale at one.
ship.location=(0,0,0); ship.rotation_euler=(0,0,0); ship.scale=(1,1,1)
# Explicit UVs are retained for downstream lightmap and external material workflows.
uv=ship.data.uv_layers.new(name="TenderUV0") if len(ship.data.uv_layers)==0 else ship.data.uv_layers.active
for poly in ship.data.polygons:
    for li in poly.loop_indices:
        v=ship.data.vertices[ship.data.loops[li].vertex_index].co
        uv.data[li].uv=((v.x+.5), (v.z+.5))
ship.data.update()

out=os.path.abspath(os.path.join(os.getcwd(),"Assets","Resources","Architecture","TenderHull.fbx"))
source=os.path.abspath(os.path.join(os.getcwd(),"Tools","Art","LandingTender.blend"))
bpy.ops.wm.save_as_mainfile(filepath=source)
# FBX stores Blender metre values with a centimetre file-unit declaration. Bake
# centimetre coordinates into the exported mesh so Unity's embedded Mesh bounds
# remain exactly one unit when BuildingVisuals extracts it from the FBX prefab.
export_mesh=ship.data.copy()
for vertex in export_mesh.vertices: vertex.co*=100.0
export_object=ship.copy(); export_object.data=export_mesh; export_object.name="TenderHull"
bpy.context.collection.objects.link(export_object)
bpy.ops.object.select_all(action='DESELECT'); export_object.select_set(True); bpy.context.view_layer.objects.active=export_object
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, global_scale=1.0,
    apply_unit_scale=False, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
    axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True,
    add_leaf_bones=False, path_mode='AUTO', embed_textures=False)
print("Exported",out,"verts",len(ship.data.vertices),"faces",len(ship.data.polygons),"slots",len(ship.data.materials),"bounds",tuple(ship.dimensions))
