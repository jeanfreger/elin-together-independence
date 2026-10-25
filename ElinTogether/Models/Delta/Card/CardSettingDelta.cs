using ElinTogether.Patches;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     What a player sets on an object of the map from its menu: the note written on it, the sale tag, who sleeps
///     in a bed and what kind of bed it is, the name of a teleporter. Each game changed its own copy only: a guest's settings never reached
///     the host, who runs the residents and keeps the save, and the host's never reached the guests <br />
///     <see cref="Fields" /> is what the tools of a held item change on an object (wrench, eco mark, brush, hammer), told by
///     the host only <br />
///     Also: a door opened or closed (by hand, or by the game, which shuts them after a while: each game did it alone), an
///     object hidden from view (visibility tool), the name given to a canvas, a mannequin, a seed <br />
///     Closed list. The whole state is sent, not the click: applying it twice changes nothing
/// </summary>
[MessagePackObject]
public class CardSettingDelta : ElinDelta
{
    public const byte Note = 0;
    public const byte Sale = 1;
    public const byte Bed = 2;
    public const byte Teleporter = 3;
    public const byte Fields = 4;
    public const byte Door = 5;
    public const byte Masked = 6;
    public const byte Name = 7;
    public const byte HouseBoard = 8;
    public const byte AltName = 9;
    public const byte Painting = 10;
    public const byte RoomPlate = 11;
    public const byte Restrain = 12;
    public const byte Look = 13;
    public const byte RideSkin = 14;
    public const byte AutoToggle = 15;
    public const byte Lock = 16;
    public const byte LightColor = 17;
    public const byte Figure = 18;
    public const byte Well = 19;
    public const byte AutoRefuel = 20;
    public const byte StockUse = 21;
    public const byte CharaSkin = 22;

    [Key(0)]
    public required RemoteCard Card { get; init; }

    [Key(1)]
    public required byte Kind { get; init; }

    [Key(2)]
    public string? Text { get; init; }

    /// <summary>
    ///     On sale or not; the kind of bed
    /// </summary>
    [Key(3)]
    public int Value { get; init; }

    /// <summary>
    ///     Those who hold the bed
    /// </summary>
    [Key(4)]
    public int[]? Holders { get; init; }

    /// <summary>
    ///     The fields of the object, in the order of <see cref="FieldsOf" />
    /// </summary>
    [Key(5)]
    public int[]? Numbers { get; init; }

    /// <summary>
    ///     The picture painted on a canvas (a small PNG, or a JPG from a camera)
    /// </summary>
    [Key(6)]
    public byte[]? Blob { get; init; }

    internal static CardSettingDelta Create(Card card, byte kind)
    {
        return new() {
            Card = card,
            Kind = kind,
            Text = kind switch {
                Note => card.c_note,
                Teleporter => (card.trait as TraitTeleporter)?.id,
                Name => card.c_refText,
                AltName => card.c_altName,
                Look => card.c_idPortrait + "\n" + card.c_idSpriteReplacer,
                RideSkin => card.c_idRidePCC,
                RoomPlate => (card.trait as TraitRoomPlate)?.areaData.name,
                _ => null,
            },
            Value = kind switch {
                Sale => card.isSale ? 1 : 0,
                Bed => (int)card.c_bedType,
                Teleporter => EClass._zone.uid,
                Door => card.dir,
                Masked => card.isMasked ? 1 : 0,
                Restrain => card.isRestrained ? 1 : 0,
                AutoToggle => card.disableAutoToggle ? 1 : 0,
                Lock => card.c_lockLv,
                LightColor => card.c_lightColor,
                Figure => card.refVal,
                Well => card.c_charges,
                AutoRefuel => card.autoRefuel ? 1 : 0,
                StockUse => card.c_isDisableStockUse ? 1 : 0,
                CharaSkin => card.idSkin,
                _ => 0,
            },
            Blob = kind == Painting ? card.c_textureData : null,
            Holders = kind == Bed ? card.c_charaList?.list.ToArray() : null,
            Numbers = kind switch {
                Fields => FieldsOf(card),
                HouseBoard => HouseOf((card.trait as TraitHouseBoard)?.data),
                RoomPlate when card.trait is TraitRoomPlate plate => [
                    plate.areaData.group, plate.areaData.maxHeight, (int)plate.areaData.accessType, plate.areaData.atrium ? 1 : 0,
                ],
                _ => null,
            },
        };
    }

    internal static int[] FieldsOf(Card c)
    {
        // a magic chest makes its (empty) upgrade when it is read, as the game does
        var up = c.trait is TraitMagicChest ? c.c_containerUpgrade : null;
        return [
            c.isWeightChanged ? c.c_weight : -1, // 0 eco mark
            c.elements.Base(652), // 1 eco mark
            c.isDyed ? c.c_dyeMat : 0, // 2 brush
            c.encLV, // 3 hammer
            c.c_containerSize, // 4 wrench: bed, chest
            up?.cap ?? 0, // 5 wrench: magic chest
            up?.cool ?? 0, // 6
            c.elements.Base(405), // 7 wrench: fridge
        ];
    }

    /// <summary>
    ///     What a player sets on a resident or an ally: its name (list of the base, "change name"), freed from its
    ///     shackle or put in one, its portrait and skin, its look picked in a dialog (sister_change), the look of the horse it rides (hitching post). Not someone
    ///     else's companion (its owner's), not a stranger
    /// </summary>
    private void ApplyChara(ElinNetBase net)
    {
        if (Card.Find() is not Chara { isDestroyed: false } chara) {
            return;
        }

        if (net is ElinNetHost host) {
            if (!host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
                (CompanionHelper.OwnerOf(chara) is { } owner ? owner != sender : !chara.IsPCFaction)) {
                return;
            }

            // a resident of the base, when only the host manages it: the sender gets the name back
            if (NetSession.Instance.Rules.HostManagesBase && !host.IsZoneSession && CompanionHelper.OwnerOf(chara) is null) {
                host.SendDeltaTo(OriginPeer, Create(chara, Kind));
                return;
            }

            host.Delta.AddRemote(this);
        }

        switch (Kind) {
            case AltName:
                chara.c_altName = Text;
                break;
            case Restrain:
                chara.isRestrained = Value != 0;
                // the shackle it stands on knows whom it holds (TraitShackle.Restrain)
                if (Value != 0 && chara.ExistsOnMap && chara.pos.FindThing<TraitShackle>() is { } shackle) {
                    shackle.owner.c_uidRefCard = chara.uid;
                }

                break;
            case Look:
                var look = (Text ?? "").Split('\n');
                chara.c_idPortrait = look[0].Length == 0 ? null : look[0];
                chara.c_idSpriteReplacer = look.Length < 2 || look[1].Length == 0 ? null : look[1];
                if (chara.ExistsOnMap) {
                    chara._CreateRenderer();
                }

                break;
            case CharaSkin:
                chara.idSkin = Value;
                if (chara.ExistsOnMap) {
                    chara._CreateRenderer();
                }

                break;
            case RideSkin:
                chara.c_idRidePCC = Text;
                if (chara.ExistsOnMap) {
                    chara._CreateRenderer();
                }

                break;
        }
    }

    /// <summary>
    ///     The house board (roof, walls, music of the lot), in the order of its fields
    /// </summary>
    internal static int[]? HouseOf(TraitHouseBoard.Data? d)
    {
        return d is null ? null : [
            d.idRoofStyle, d.height, d.idBGM, d.heightFix, d.idRoofTile, d.idBlock, d.idRamp, d.idDeco, d.idDeco2,
            d.decoFix, d.decoFix2, d.colRoof, d.colBlock, d.colDeco, d.colDeco2, d.reverse ? 1 : 0, d.snow ? 1 : 0,
            d.altRoof ? 1 : 0,
        ];
    }

    internal static List<(Thing Thing, int[] Fields)> Snapshot(Point pos)
    {
        return pos.Things.Where(t => t.IsInstalled).Select(t => (t, FieldsOf(t))).ToList();
    }

    /// <summary>
    ///     After the tool: tell the others about the objects of the tile that it changed
    /// </summary>
    internal static void TellChanged(List<(Thing Thing, int[] Fields)> before)
    {
        foreach (var (thing, old) in before) {
            if (!thing.isDestroyed && !FieldsOf(thing).SequenceEqual(old)) {
                CardSettingEvent.Tell(thing, Fields);
            }
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Kind is AltName or Restrain or Look or RideSkin or CharaSkin) {
            ApplyChara(net);
            return;
        }

        // an object of the map, or one the sender carries; a bed setting only on a bed
        if (Card.Find() is not Thing { isDestroyed: false } thing || (Kind == Bed && thing.trait is not TraitBed) ||
            (Kind == Teleporter && thing.trait is not TraitTeleporter) || (Kind == Door && thing.trait is not TraitDoor) ||
            (Kind == HouseBoard && thing.trait is not TraitHouseBoard) ||
            (Kind == Painting && (thing.trait is not TraitCanvas || Blob is not { Length: <= 1 << 20 })) ||
            (Kind == RoomPlate && thing.trait is not TraitRoomPlate) || (Kind == Figure && (thing.trait is not TraitFigure { source: { } row } || Value < 0 || Value >= row.tiles.Length)) || (Kind == Well && thing.trait is not TraitWell) || Kind is > RoomPlate and < AutoToggle or > StockUse) {
            return;
        }

        if (net is ElinNetHost host) {
            // the fields are the host's to tell
            if (Kind == Fields || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
                (thing.GetRootCard() is Chara holder && holder != sender)) {
                return;
            }

            // only the host manages the base: what stands on its map is not changed by the others, the sender gets the
            // setting the host keeps, which puts its copy back (what a player carries is its own)
            // (a door is opened by whoever walks there)
            if (NetSession.Instance.Rules.HostManagesBase && !host.IsZoneSession && thing.GetRootCard() is not Chara && Kind is not (Door or Well)) {
                host.SendDeltaTo(OriginPeer, Create(thing, Kind));
                return;
            }

            host.Delta.AddRemote(this);
        }

        switch (Kind) {
            case Note:
                thing.c_note = Text;
                break;
            case Sale:
                thing.SetSale(Value != 0);
                break;
            case Bed:
                thing.c_bedType = (BedType)Value;
                thing.c_charaList = Holders is { Length: > 0 } ? new CharaList { list = [..Holders] } : null;
                break;
            case Teleporter:
                // the name of a teleporter, and the register of each game that links the ones of the same name
                var teleporter = (TraitTeleporter)thing.trait;
                teleporter.id = Text;
                game.teleports.SetID(teleporter, Value);
                break;
            case Door when thing.dir != Value:
                thing.dir = Value;
                thing.renderer.RefreshSprite();
                if (thing.pos.IsValid && EClass._map is { } map) {
                    map.RefreshSingleTile(thing.pos.x, thing.pos.z);
                    map.RefreshFOV(thing.pos.x, thing.pos.z);
                }

                break;
            case Masked:
                thing.isMasked = Value != 0;
                break;
            case Name:
                thing.c_refText = Text;
                break;
            case Fields when Numbers is { Length: 8 } n:
                if (n[0] >= 0) {
                    thing.ChangeWeight(n[0]);
                }

                if (n[1] > 0) {
                    thing.elements.SetBase(652, n[1]);
                }

                thing.Dye(sources.materials.map.TryGetValue(n[2]));
                if (thing.encLV != n[3]) {
                    thing.SetEncLv(n[3]);
                }

                if (thing.trait is TraitBed) {
                    // a counter for a bed (TraitWrench), not width and height
                    thing.c_containerSize = n[4];
                } else if (thing.c_containerSize != n[4]) {
                    thing.things.SetSize(n[4] / 100, n[4] % 100);
                }

                if (thing.trait is TraitMagicChest) {
                    var up = thing.c_containerUpgrade;
                    (up.cap, up.cool) = (n[5], n[6]);
                }

                if (n[7] > 0) {
                    thing.elements.SetBase(405, n[7]);
                }

                break;
            case Well:
                thing.c_charges = Value;
                break;
            case AutoRefuel:
                thing.autoRefuel = Value != 0;
                break;
            case StockUse:
                thing.c_isDisableStockUse = Value != 0;
                break;
            case LightColor:
                thing.c_lightColor = Value;
                if (thing.ExistsOnMap) {
                    thing.RecalculateFOV();
                    thing.renderer.GetTC<TCExtra>()?.RefreshColor();
                }

                break;
            case Figure:
                thing.refVal = Value;
                if (thing.ExistsOnMap) {
                    thing.renderer.RefreshSprite();
                }

                break;
            case AutoToggle:
                thing.disableAutoToggle = Value != 0;
                break;
            case Lock:
                thing.c_lockLv = Value;
                // what Trait.OnLockOpen undoes too: a hard lock and the price it added
                if (Value == 0 && thing.c_lockedHard) {
                    thing.c_lockedHard = false;
                    thing.c_priceAdd = 0;
                }

                break;
            case Painting when Blob is not null:
                thing.c_textureData = Blob;
                thing.isModified = true;
                thing.ClearPaintSprite();
                thing.renderer.RefreshSprite();
                break;
            // (the values the menus offer: groups 0-4, heights 1-9; with some room to spare)
            case RoomPlate when Numbers is { Length: 4 } n && n[0] is >= 0 and <= 9 && n[1] is >= 0 and <= 20 &&
                                System.Enum.IsDefined(typeof(BaseArea.AccessType), n[2]):
                var area = ((TraitRoomPlate)thing.trait).areaData;
                area.name = Text;
                (area.group, area.maxHeight, area.accessType, area.atrium) = (n[0], n[1], (BaseArea.AccessType)n[2], n[3] != 0);
                // each game rebuilds its rooms from their plates
                if (thing.IsInstalled && thing.parent == EClass._zone) {
                    EClass._map.rooms.RefreshAll();
                }

                break;
            case HouseBoard when Numbers is { Length: 18 } n:
                var board = (TraitHouseBoard)thing.trait;
                var d = board.data ??= new();
                (d.idRoofStyle, d.height, d.idBGM, d.heightFix, d.idRoofTile, d.idBlock, d.idRamp, d.idDeco, d.idDeco2) =
                    (n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8]);
                (d.decoFix, d.decoFix2, d.colRoof, d.colBlock, d.colDeco, d.colDeco2) = (n[9], n[10], n[11], n[12], n[13], n[14]);
                (d.reverse, d.snow, d.altRoof) = (n[15] != 0, n[16] != 0, n[17] != 0);
                board.ApplyData();
                break;
        }
    }
}
