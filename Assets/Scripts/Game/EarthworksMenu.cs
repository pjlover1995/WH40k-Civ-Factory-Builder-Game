using UnityEngine;
using UnityEngine.UI;
using WH30K.Gameplay.Construction;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject earthworksPanel;
        private Text earthworksTitle, earthworksHelp, earthworksStatus, earthworksRadius, earthworksStep;
        private Button earthworksRemove, earthworksDeploy, earthworksLocate;
        private readonly Button[] earthworksModes = new Button[4];
        private EarthworksController Earthworks => GetComponent<EarthworksController>();

        private void BuildEarthworks(Transform parent)
        {
            earthworksPanel=Panel("EarthworksHUD",parent,new Vector2(294,444),new Vector2(-16,-114),TextAnchor.UpperRight);
            earthworksPanel.GetComponent<Image>().color=new Color(.025f,.075f,.085f,.98f);
            earthworksTitle=Label("EarthworksTitle",earthworksPanel.transform,"ENGINEERING / EARTHWORKS",12,new Vector2(16,-16),240,24,EarthworksController.ColorFor(EarthworkMode.Flatten));
            Button("CloseEarthworks",earthworksPanel.transform,new Vector2(252,-12),"X",out _,26,26).onClick.AddListener(()=>Earthworks.Close());
            earthworksHelp=Label("EarthworksHelp",earthworksPanel.transform,"",12,new Vector2(16,-50),262,100,Ivory);
            var labels=new[]{"MOVE","FLATTEN","RAISE","LOWER"};
            for(var i=0;i<4;i++)
            {
                var mode=(EarthworkMode)i;
                earthworksModes[i]=Button("Earthwork"+mode,earthworksPanel.transform,new Vector2(16+(i%2)*134,-158-(i/2)*36),labels[i],out var label,126,30);
                label.color=EarthworksController.ColorFor(mode);
                earthworksModes[i].onClick.AddListener(()=>Earthworks.SetMode(mode));
            }
            Button("EarthworkRadius",earthworksPanel.transform,new Vector2(16,-234),"",out earthworksRadius,126,30).onClick.AddListener(()=>Earthworks.CycleRadius());
            Button("EarthworkStep",earthworksPanel.transform,new Vector2(150,-234),"",out earthworksStep,126,30).onClick.AddListener(()=>Earthworks.CycleStep());
            earthworksDeploy=Button("DeployDozer",earthworksPanel.transform,new Vector2(16,-272),"DEPLOY DOZER",out _,126,30);
            earthworksDeploy.onClick.AddListener(()=>Earthworks.Deploy());
            earthworksLocate=Button("LocateDozer",earthworksPanel.transform,new Vector2(150,-272),"LOCATE",out _,126,30);
            earthworksLocate.onClick.AddListener(()=>{
                var v=Earthworks.Vehicle; if(v!=null)Orbit?.FocusSettlement((v.position-bootstrap.ActivePlanet.transform.position).normalized);
            });
            earthworksRemove=Button("ConfirmBulldoze",earthworksPanel.transform,new Vector2(16,-310),"REMOVE BUILDING / NO REFUND",out var removeLabel,260,30);
            removeLabel.color=EarthworksController.ColorFor(EarthworkMode.Demolish);
            earthworksRemove.onClick.AddListener(()=>Earthworks.RemoveTarget());
            Button("StopDozer",earthworksPanel.transform,new Vector2(16,-348),"STOP DOZER",out _,126,28).onClick.AddListener(()=>Earthworks.Stop());
            Button("ExitEarthworks",earthworksPanel.transform,new Vector2(150,-348),"EXIT [ESC]",out _,126,28).onClick.AddListener(()=>Earthworks.Close());
            earthworksStatus=Label("EarthworksStatus",earthworksPanel.transform,"",11,new Vector2(16,-386),262,52,Muted);
            earthworksPanel.SetActive(false);
        }

        private void RefreshEarthworks()
        {
            var tools=Earthworks;
            var open=tools!=null && tools.IsOpen;
            if(earthworksPanel.activeSelf!=open)earthworksPanel.SetActive(open);
            if(!open)return;
            if(inspector.activeSelf)selection.Select(null);
            var demolish=tools.Mode==EarthworkMode.Demolish;
            earthworksTitle.text=demolish?"ENGINEERING / BULLDOZE":"PRAETOR / "+tools.Mode.ToString().ToUpperInvariant();
            earthworksTitle.color=tools.ToolColor;
            earthworksPanel.GetComponent<Outline>().effectColor=tools.ToolColor;
            earthworksHelp.text=demolish?tools.DemolitionName+"\n\nRemoves the whole building or merged farm estate, including all sites, modules, jobs, housing, local food/materials and linked cargo. Core buildings are protected.":
                "Select a mode, then left-click terrain. The tracked dozer drives into range and works for 4 simulation seconds.\nCyan: flatten at point height\nAmber: raise / Violet: lower";
            earthworksRadius.text=$"RADIUS {tools.BrushRadius:0} m";
            earthworksStep.text=$"STEP {tools.StepHeight:0} m";
            earthworksStatus.text=tools.Status;
            earthworksRemove.gameObject.SetActive(demolish); earthworksRemove.interactable=tools.CanDemolish;
            earthworksDeploy.interactable=!tools.HasVehicle && !demolish;
            earthworksLocate.interactable=tools.HasVehicle;
            for(var i=0;i<4;i++)
            {
                earthworksModes[i].interactable=!demolish;
                var colors=earthworksModes[i].colors;
                colors.normalColor=(int)tools.Mode==i?Color.Lerp(ButtonBase,tools.ToolColor,.28f):ButtonBase;
                earthworksModes[i].colors=colors;
            }
        }
    }
}
