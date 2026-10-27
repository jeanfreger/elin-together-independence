# Elin Together — version « indépendance »

**Version actuelle : 0.26.1082** (zip `ElinTogether-independence.zip`).

[English](README.md) | Français | [中文](README_zh.md) | [日本語](README_ja.md)

[![Dernière version](https://img.shields.io/github/v/release/jeanfreger/elin-together-independence?include_prereleases&label=derni%C3%A8re%20version)](https://github.com/jeanfreger/elin-together-independence/releases)

> **Pas encore de serveur ni de Discord officiels.** Elin Together n'a pour l'instant ni serveur public officiel ni Discord officiel. Personne du projet ne vous demandera jamais d'argent, de mot de passe ou vos identifiants Steam : quelqu'un qui le fait au nom du mod n'est pas nous. Le mod et ses mises à jour sont uniquement sur cette page et sur le Steam Workshop.

Une version modifiée d'[Elin Together](https://github.com/ElinTogether/ElinTogether), le mod multijoueur
d'[Elin](https://store.steampowered.com/app/2135150/Elin/), avec un seul but : **en jeu, aucune différence entre
l'host et les autres joueurs**. Chacun va où il veut, avec ses compagnons, ses quêtes, sa renommée et son argent ;
le monde (histoire, base, guildes) reste commun. Et le monde lui-même n'a plus besoin de vivre sur le PC d'un seul
joueur.

Le mod d'origine garde tout le groupe sur la carte de l'host et traite les autres joueurs comme ses coéquipiers.
Ses auteurs ne prévoient pas les cartes séparées ; cette version est l'endroit où on l'essaie. Tout le mérite du
mod lui-même leur revient (voir [Crédits](#crédits)).

> **État : expérimental, pas prêt pour une vraie sortie.** Chaque version est une préversion, compilée pour le canal
> Nightly d'Elin. L'essentiel est vérifié par des tests automatiques en jeu, sur un seul PC (deux à cinq fenêtres
> du jeu). Un seul petit groupe y a joué pour de vrai, et chaque soirée a trouvé des défauts que les tests n'avaient
> pas vus. Plusieurs corrections récentes ont été publiées **sans avoir été jouées du tout**. Ce qui manque avant une
> vraie sortie est listé dans [Ce qu'il reste avant une vraie sortie](#ce-quil-reste-avant-une-vraie-sortie).
> Faites d'abord une copie de vos sauvegardes : `%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin`.

**Sommaire**

1. [Ce que ça change](#ce-que-ça-change)
2. [Installer](#installer)
3. [Votre première partie ensemble](#votre-première-partie-ensemble)
4. [Garder le monde en ligne : le dépôt](#garder-le-monde-en-ligne--le-dépôt)
5. [Les parties avec mods](#les-parties-avec-mods)
6. [Réglages](#réglages)
7. [Comment ça marche](#comment-ça-marche)
8. [Limites connues](#limites-connues)
9. [Ce qu'il reste avant une vraie sortie](#ce-quil-reste-avant-une-vraie-sortie)
10. [Comment c'est testé](#comment-cest-testé)
11. [En cas de problème](#en-cas-de-problème)
12. [Compiler](#compiler)
13. [Crédits](#crédits)

## Ce que ça change

### Chacun va où il veut

- **Voyage indépendant.** Un joueur part sur une autre carte sans l'host. La carte et ce qu'il y fait sont
  conservés, sa progression est sauvée chaque minute, et le chat marche entre toutes les cartes.
- **Cartes partagées.** Un joueur peut en rejoindre un autre sur sa carte, host ou non. Si celui qui tient la carte
  part, un autre la reprend.
- **Personne n'est traîné.** Quand l'host change de carte, les autres restent où ils sont. Un joueur entre sur une
  carte par son entrée ; personne n'est téléporté sur l'host.
- **Chacun à son rythme.** Un monstre agit quand le joueur qu'il combat agit, pas au rythme de l'host, et un invité
  marche aussi régulièrement que l'host.
- **Votre temps est à vous.** Celui qui se couche dort tout de suite ; la nuit passe pour le monde quand assez de
  joueurs dorment en même temps (l'host choisit : tous, la moitié, ou un seul). Un joueur loin du clavier ne retient
  plus la nuit. Quand un autre joueur voyage ou dort, la date avance, mais vous n'avez pas plus faim, votre
  nourriture ne pourrit pas et vos délais de quête ne bougent pas.
- **Les grands mondes.** Le temps peut s'arrêter quand personne ne joue, et une longue absence est rattrapée par
  étapes (7 jours au plus). Chaque joueur du monde figure dans le classement des aventuriers.

### À chaque joueur le sien

- **Les compagnons** suivent le joueur qui les a recrutés, de quelque façon que ce soit (dialogue, boule à monstre,
  monture, animal acheté), voyagent avec lui, comptent dans sa limite d'alliés et obéissent à ses consignes.
- **Quêtes aléatoires, renommée et karma.** Cinq quêtes chacun, avec leur récompense. Le crime aussi : c'est le
  joueur fautif qui perd du karma, et les gardes ne poursuivent que lui.
- **L'argent.** L'argent d'une vente par la caisse d'expédition va à celui qui y a mis l'objet ; un invité paie
  une facture avec son propre or.
- **Votre personnage.** En rejoignant, vous retrouvez le personnage que vous jouiez, sans aucune question ; un
  nouveau joueur crée le sien sur l'écran de création du jeu. L'host peut aussi permettre d'amener un personnage
  d'une sauvegarde solo.
- **Vos réglages.** Fenêtres, combat automatique, tri du sac et consignes des compagnons restent les vôtres à
  chaque changement de carte et à chaque reconnexion.
- **Le combat automatique protège les siens.** Le combat automatique du jeu sauve d'un coup mortel le joueur qui le
  lance et ses compagnons (avec la compétence de tactique et l'endurance de ce joueur). Avant, celui de l'host
  protégeait tout le monde et celui d'un invité personne. Un compagnon qui n'appartient à personne n'est protégé
  par personne.
- **Votre titre.** Le titre (alias) que vous avez choisi est le vôtre : un invité ne reçoit plus celui de l'host, et
  garde le sien d'une session à l'autre.

### Un seul monde pour tous

- **L'histoire.** Les quêtes d'histoire sont dans un seul journal : n'importe qui les lance, les avance, les
  termine. Dialogues déjà vus, objets clés et dette sont communs.
- **La base.** Un invité construit comme l'host (sols, murs, meubles, miner, creuser, couper, zones, outil de
  terrain) et ce que l'un construit se voit chez l'autre tout de suite. Recherche, compétences du foyer,
  politiques, résidents et réglages de coffre peuvent être gérés par n'importe quel joueur. L'host peut garder la
  base pour lui avec une case.
- **Guildes, affinité, codex, banque.** Rejoindre une guilde vaut pour le groupe, l'affinité d'un habitant est la
  même pour tous, les cartes qu'un joueur collecte arrivent dans le codex de tous, et la banque montre le même
  contenu partout.
- **Quêtes à donjon.** N'importe quel joueur peut prendre une quête qui a sa propre zone. Quand il part, l'autre
  voit une boîte Oui/Non pour l'accompagner : la zone est commune et la récompense va à celui qui a pris la quête.
- **La musique des cartes.** La musique choisie pour une carte dans sa fenêtre de liste de lecture est la même pour
  tous les joueurs présents sur cette carte.
- **Entre joueurs.** Une fenêtre d'échange (chacun met des objets et de l'or, les deux confirment ; un objet porté
  est retiré quand on l'échange), des duels où personne ne meurt et où rien n'est perdu (avec un *Give up* dans
  votre propre menu), et pas de meurtre hors duel.
- **Permadeath entre amis** (option de l'host, cochée au départ, mondes en permadeath seulement) : un joueur qui
  meurt attend dans le monde au lieu que la partie s'arrête, et n'importe quel autre joueur le ressuscite chez un
  barman au prix du jeu. Rien n'est effacé, même quand c'est l'host qui meurt.

### Équipes (facultatif)

Décoché au départ (case de l'host *Teams*). Quand l'host les active, les joueurs forment des équipes : chacune a sa
terre, ses coffres, ses bâtiments et ses portes, un titre de propriété pour son fondateur et, si l'host le permet,
du PvP entre équipes. Les coffres d'une autre équipe ne servent pas, même pour fabriquer (l'host le refuse aussi).
Un nouveau joueur apparaît dans la zone d'arrivée : la prairie du début, la carte de l'host, ou une zone donnée par
son nom ou son numéro. Sans la case, rien ne change.

### Temps et réglages simplifiés

- **Temps sur la carte du monde, trois modes** (choix de l'host) : *Horloge commune* (une seule horloge, la carte du
  monde n'attend personne), *Voyager ensemble* (le groupe avance d'un bloc sur la carte du monde, on attend un
  joueur encore en chargement) et *Rythme personnel* (chacun voyage à son rythme, les autres rattrapent au
  regroupement).
- **Réglages réorganisés.** Des préréglages et des choix simples en haut (« Coop recommandée », « Classique mené par
  l'host » ou Personnalisé), toutes les cases gardées dans une section *Avancé*, une description sous chaque case,
  les cases dépendantes grisées, « Revenir au recommandé » et « Annuler ». Les amis voient « Règles de ce monde » en
  lecture seule. La même page est dans la page de config des mods du jeu.

### Le monde lit chaque joueur

- **Le contenu du monde vient de chaque joueur, pas seulement de l'host.** Tableaux de la ville (palier de
  renommée, danger, récompenses), stock des boutiques et étagères spéciales, apparitions sur la carte, tonneaux,
  étagères d'affinage et lait maternel suivent les joueurs présents sur la carte.
- **Base et vie de la ville pour chaque joueur.** Raids, mois de chance, jours avec le joueur et pluie sur une ferme
  comptent chaque joueur ; habitants, prêtres, boss de nefia, rumeurs et pierres agissent autour de chaque joueur
  selon son karma ; la mort d'un boss crédite la profondeur à chaque joueur de la carte ; les invocations suivent
  leur propre maître.
- **Chance, journaux et compteurs de chaque joueur** (tirages de chance, niveau de reconstruction, journaux, trésor,
  recettes connues, croquis, airs, points de résurrection) restent à lui d'une copie du monde à l'autre. Un
  recrutement n'est payé qu'une fois.
- **Combat auto par joueur**, mêmes règles de combat pour tous (points faibles, plancher d'invocation, progression
  du lancer, vitesse) et duel aux règles du jeu.
- **La difficulté suit le joueur.** La difficulté d'une quête suit le joueur qui la prend.

### Un invité joue comme un joueur solo

Des dizaines de corrections pour que ce qui marche pour l'host marche pour un invité : mort et testament, cadeaux
du dieu, autels, pièges, grimoires, guérisseur, bénédictions, investir, runes, rangement automatique, outils tenus
en main, copie chez Kettle, résurrection d'un compagnon chez le barman, fenêtres qui s'ouvraient chez l'host, poteaux d'attache, compagnon d'un
invité qui utilise l'exploit de récupération de l'invité sur les pièges, habitant de l'histoire qui se tient près de
l'invité qui a fini la quête, « le jour se termine » lu par les invités, inventaire à « toujours trier » qui figeait
le jeu d'un invité, invité qui quitte en étant ailleurs et n'est plus pris pour un lien perdu… Chaque invité garde
aussi ses propres scènes d'histoire, sa liste de mécènes et son compte de Churyu d'une copie du monde à l'autre. La
liste complète est gardée dans les notes du développeur, elle n'est pas publiée.

### Le monde ne dépend plus du PC de l'host

- **Un dépôt garde le monde** : un dépôt GitHub privé, un dossier partagé, ou un petit logiciel serveur. Le
  premier arrivé prend le monde et l'héberge, chaque sauvegarde y retourne, et quand il part le suivant prend la
  suite.
- **Un monde qui change de main.** Celui qui reprend le monde joue son propre personnage ; l'ancien host retrouve
  le sien quand il rejoint. Si quelqu'un héberge déjà, vous êtes envoyé dans sa partie au lieu d'en ouvrir une
  copie.
- **L'host n'a plus rien à faire.** Le monde se sauvegarde tout seul toutes les 2 minutes quand d'autres jouent, et
  un monde déjà partagé s'ouvre aux amis Steam dès qu'il est chargé.
- **Une coupure est sans conséquence.** Un invité qui perd la connexion revient tout seul dans la partie (il
  essaie pendant 3 minutes).
- **Les jeux restent d'accord.** Aucun message n'est jeté quand la liaison est saturée. Toutes les 2 secondes les
  jeux comparent quelques nombres par carte, et une carte dont l'écart dure est rechargée sur place.
- **Des données réseau plus sûres** (reprises du mod d'origine). Ce qu'un autre jeu envoie n'est lu que comme les
  types de données du jeu et de ses mods ; tout le reste est refusé.
- **Un serveur dédié**, comme un serveur Minecraft : un Elin que personne ne joue fait tourner le monde, joignable
  par Steam ou par adresse. Il fait une sauvegarde du monde toutes les heures, et l'application *Elin Together
  Server* le relance s'il plante, restaure une sauvegarde, démarre avec Windows et montre les joueurs, le journal
  d'administration et des graphiques en direct.
- **L'host part proprement.** Quand l'host quitte pour de bon, les invités en sont informés au lieu d'attendre
  devant un écran figé.

### Serveurs publics : administration et sécurité

- **Modération.** Expulser avec un motif (le joueur ne peut pas revenir pendant quelques minutes), bannir et rendre
  muet par compte Steam (gardé dans le monde), des admins nommés par l'host qui ont les boutons Kick, Mute et Ban
  dans l'onglet *Players*, et un mode *Members only*. Chaque action est écrite dans `ElinMP/Logs/admin.log` ; un
  serveur dédié reçoit les mêmes ordres de son application ou de `ElinMP/server.cmd`.
- **Sécurité contre les jeux modifiés.** L'host refuse ce qu'un invité n'a pas le droit de faire (objets et
  personnages des autres joueurs, or ou objets inventés, chat sous un autre nom), toujours dans une partie publique
  et sur un serveur dédié (case *Refuse acts on what others own* sinon). Un paquet ou une carte fabriqués ne peuvent
  plus faire planter l'host ni exécuter du code.
- **Beaucoup de joueurs.** Chaque joueur ne reçoit que ce qui concerne sa propre carte, les joueurs qui arrivent
  ensemble partagent une seule copie du monde, et la charge a été mesurée avec jusqu'à 50 joueurs simulés.

### Les mêmes mods pour tous

- **Les mods de la partie arrivent tout seuls** quand vous rejoignez, et Elin se relance une fois avec eux. Un
  joueur peut choisir de les garder : cette relance n'arrive alors qu'une fois, et plus jamais pour cette partie.
- **Des versions d'Elin différentes peuvent jouer ensemble.** Seule la version du mod doit être la même pour
  tous ; un joueur sur une autre version d'Elin entre avec un avertissement.

Presque tout cela est une **case à cocher côté host** : décochée, le mod se comporte comme l'original. Voir
[Réglages](#réglages).

## Installer

Il faut Elin sur Steam, sur le canal **Nightly** (Steam → clic droit sur Elin → Propriétés → Bêtas), et le mod
[YK Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3400020753). S'abonner à
[Elin Together](https://steamcommunity.com/sharedfiles/filedetails/?id=3773298709) sur le Workshop l'amène avec
lui. Lancez Elin une fois après l'abonnement, puis fermez-le.

1. Téléchargez `ElinTogether-independence.zip` sur la
   [page des versions](https://github.com/jeanfreger/elin-together-independence/releases).
2. Décompressez tout le dossier et lancez `Install.bat`. Il trouve Elin, copie le mod dans
   `Elin\Package\Mod_ElinTogether` et remplace la version Workshop par celle-ci dans la liste de mods du jeu.
3. Lancez Elin par Steam.

`Uninstall.bat` remet la version Workshop ; rien n'est supprimé, l'ancien état est gardé dans
`Elin\_ElinTogether_sauvegarde`. La notice `README.txt` du zip explique tout.


**Tous les joueurs doivent installer la même version.** Ce fork ne se connecte pas à la version du Workshop, ni à
une autre version de lui-même : un joueur sur une autre version est refusé, avec un message qui nomme les deux
versions. Chaque version est compilée pour une version d'Elin, écrite dans son titre.

**Mettre à jour** : téléchargez le nouveau zip et relancez `Install.bat`. Vos réglages sont gardés.

## Votre première partie ensemble

Le panneau du mod s'ouvre par le bouton *Elin Together* de l'écran titre, ou par Échap → Mods → Elin Together en
jeu. Ses onglets : *Lobby*, *Server Setting* (les règles de l'host) et *Client Settings*, plus *Session Info* une
fois la partie ouverte. Le panneau est en anglais, japonais ou chinois, pas en français.

**L'host**

1. Commence une nouvelle partie ou charge une sauvegarde. Une partie ne peut être ouverte aux autres que si le
   monde a un **terrain revendiqué** : dans une partie neuve, parlez à Ashland, ramassez l'acte qu'il laisse,
   lisez-le et répondez oui.
2. Ouvre la partie : *Start Server* dans l'onglet Lobby. Un monde déjà partagé s'ouvre tout seul aux amis Steam
   quand il est chargé.
3. Invite : *Invite Friend* dans l'onglet Lobby, ou laisse ses amis utiliser *Rejoindre la partie* sur son nom
   dans la liste d'amis Steam.

**Un invité**

1. Rejoint depuis l'écran titre : accepte une invitation Steam, utilise *Rejoindre la partie* dans la liste d'amis
   Steam, ou choisit la partie dans l'onglet Lobby. *Join by address* sert pour un serveur.
2. La première fois dans ce monde, crée un personnage sur l'écran de création du jeu. Ensuite il le retrouve sans
   aucune question.
3. Si la partie a des mods du Workshop que l'invité n'a pas, Elin se ferme et se relance une fois avec eux, puis
   revient tout seul dans la partie : voir [Les parties avec mods](#les-parties-avec-mods). **Ce n'est pas un
   plantage.**

**Ensuite**

- Chacun joue comme en solo : sortir seul de la carte, prendre des quêtes au tableau, construire dans la base,
  dormir quand il veut.
- Les règles de l'host sont dans *Server Setting* ; un changement s'applique tout de suite, pour tous.
- Quand l'host part, la partie s'arrête pour tout le monde, sauf si le monde est dans un dépôt (section suivante).

## Garder le monde en ligne : le dépôt

Un dépôt garde le monde commun en dehors des sauvegardes d'un joueur : le groupe ne dépend plus de la présence
d'une seule personne. Chaque joueur qui peut héberger le règle une fois, dans *Client Settings* : **Depot**, et
**Depot key** quand il y a un mot de passe ou une clé. Un ami qui ne fait que rejoindre n'a besoin ni de l'un ni
de l'autre.

| Dépôt | Ce qu'il faut | Réglage « Depot » |
|---|---|---|
| Dépôt GitHub privé | Un compte GitHub pour un joueur du groupe. Aucun PC à laisser allumé, aucun port à ouvrir. | `github:proprietaire/depot` |
| Elin Together Server (`ElinTogetherServer.exe`, dans le zip) | Un PC qui reste allumé. Elin n'a pas besoin d'y être installé. Port TCP 55557. | `adresse:55557`, ou simplement *Join by address* |
| Dossier partagé | Un dossier que tous les joueurs peuvent atteindre, partagé ou synchronisé. | Le chemin du dossier |

Ensuite, dans l'onglet Lobby :

- **Put this save on the server**, une partie chargée et pas encore ouverte aux autres : cette partie devient le
  monde du dépôt. Le jeu revient à l'écran titre et recharge le monde depuis le dépôt : à partir de là, c'est ce
  monde-là que vous jouez.
- **Take the world from the server and host it**, depuis l'écran titre : vous prenez le dernier monde et vous
  l'hébergez ; les autres vous rejoignent par Steam. Si quelqu'un héberge déjà, le bouton donne son nom et vous
  fait entrer dans sa partie.
- Chaque sauvegarde retourne au dépôt, au plus toutes les 5 minutes et une dernière fois en quittant. Quand l'host
  part, n'importe qui peut prendre le monde. Si l'host plante, le monde se libère tout seul au bout de 3 minutes,
  et jusqu'à 5 minutes de jeu peuvent être perdues.

**Avec GitHub**, le joueur qui possède le dépôt :

1. crée sur github.com un dépôt vide et **privé**, uniquement pour ce monde (le mod refuse un dépôt public) ;
2. crée une clé d'accès (Settings → Developer settings → Fine-grained tokens) limitée à ce dépôt, avec
   *Contents : Read and write* ;
3. écrit `github:proprietaire/depot` dans **Depot** et colle la clé dans **Depot key** ;
4. donne les deux aux amis qui peuvent héberger. Ils n'ont pas besoin de compte GitHub.

La clé reste en clair dans le fichier de réglages du mod sur chaque PC ; elle n'ouvre que ce dépôt et se révoque
d'un clic. Un monde de plus de 20 Mo en zip est refusé. GitHub garde chaque version du monde : c'est une
sauvegarde de secours gratuite, et cela veut aussi dire que le dépôt grossit à chaque envoi, d'environ un petit
commit par minute tant que quelqu'un héberge et d'une copie du monde toutes les 5 minutes. Quand il devient gros,
supprimez-le et créez-en un nouveau. Le dépôt garde aussi `modlist.txt`, les mods du monde.

**Serveur dédié.** `ElinTogetherServer.exe` a un second mode, « With Elin on this PC: the world runs all the
time » : Elin tourne derrière, sans fenêtre ; les joueurs rejoignent par le salon Steam du serveur ou par
*Join by address* (`adresse:55556`, UDP). L'application relance Elin s'il s'arrête tout seul, garde une sauvegarde
du monde toutes les heures (restaurée en un clic à l'arrêt), peut démarrer avec Windows (`--elin <save>`), et a des
pages pour la modération (expulser, rendre muet, bannir, un message à tous), le journal d'administration et l'état
du serveur (joueurs, réseau, mémoire). `Server.bat` fait la même chose avec une fenêtre de jeu. Windows seulement.

## Les parties avec mods

Elin charge ses mods au démarrage : un joueur ne peut donc pas recevoir un mod en cours de route. Voici ce que le
mod fait à la place.

**Quand vous rejoignez** (ou prenez un monde dans un dépôt), le mod compare vos mods à la liste de référence : le
`modlist.txt` du dépôt quand le monde en vient, sinon les mods de l'host. S'il vous manque des mods du Workshop :

1. Steam les télécharge, sans que votre compte s'abonne à quoi que ce soit ;
2. Elin se ferme et se relance **une fois** avec exactement les mods de la partie ;
3. il vous ramène tout seul dans la partie. S'il ne se relance pas, relancez Elin à la main dans la demi-heure.

Au départ, votre propre liste de mods revient au lancement suivant d'Elin. Le prix : Elin se relance de nouveau à
la première connexion de **chaque** lancement. Deux façons de l'éviter :

- **Cochez « Keep the mods of the game (subscribe on the Workshop) »** dans *Client Settings*. Votre compte Steam
  s'abonne aux mods de la partie et ils restent allumés dans votre liste. Elin se relance encore une fois, puis
  plus jamais pour cette partie, même après avoir fermé le jeu. Pour revenir en arrière, désabonnez-vous sur le
  Workshop ou éteignez ces mods dans le Mod Viewer.
- Ou abonnez-vous vous-même à ces mods sur le Workshop.

Pour garder vos mods et seulement être prévenu des différences, décochez « Fetch the mods of the game by itself ».

À savoir :

- Un mod installé à la main, hors Workshop, ne peut pas être téléchargé : son nom est affiché.
- Le mod qui apporte la langue dans laquelle vous lisez le jeu n'est jamais éteint.
- Un refus pour cause de mods les nomme : ceux qui manquent, ceux à installer à la main, ceux en trop.
- **Ces mods sont choisis par l'host et leur code tourne sur votre PC : à utiliser avec des gens de confiance.**
- AutoAct et Dynamic Riding sont pris en charge. Pour les autres mods, la compatibilité est celle de l'original.

## Réglages

### Host : l'onglet *Server Setting*

Chaque nouveau comportement a sa case, avec une ligne d'explication en jeu. Les règles sont celles de l'host et
s'appliquent à tous dès qu'elles changent. Les noms des cases sont en anglais dans le jeu.

| Case | Au départ | Cochée |
|---|---|---|
| Independent travel | cochée | Chacun va où il veut, seul ou ensemble. Décochée : tout le monde suit l'host. |
| Players choose their character when joining | décochée | En rejoignant, le joueur choisit un de ses personnages de ce monde, ou en crée un. Décochée : le dernier joué. |
| Players can bring a character from their own saves | décochée | Le personnage, son équipement, son sac, son or, sa renommée et son karma. Pas ses compagnons, sa base, ses quêtes ni sa banque. La sauvegarde est seulement lue. |
| Revive at the bartender (permadeath) | cochée | Mondes en permadeath seulement : un joueur mort attend dans le monde et n'importe quel joueur le ressuscite chez un barman. Décochée : le permadeath du jeu (la mort de l'host efface le monde). |
| Public game | décochée | N'importe qui qui trouve la partie peut la rejoindre, pas seulement les amis ; les contrôles de sécurité sont toujours actifs. |
| Kicked players wait | 5 min | Un joueur expulsé ne peut pas revenir pendant ce temps (Off, 5, 15, 60 min). |
| Members only | décochée | Seuls les joueurs de ce monde (et les comptes admis par `allow`) peuvent rejoindre. |
| Refuse acts on what others own | décochée | Les actes d'un invité sur les personnages et objets des autres, et les déluges de données, sont refusés. Toujours actif dans une partie publique et sur un serveur dédié. |
| Random quests, fame and karma per player | cochée | 5 quêtes aléatoires chacun, avec récompense, renommée et karma. Les quêtes d'histoire restent communes. |
| Shipping per player | cochée | L'argent de l'expédition va à celui qui a déposé l'objet. Décochée : tout va à l'host. |
| Trade window between players | cochée | Clic sur un autre joueur pour échanger objets et or. Les deux confirment. |
| Other players can use build mode | cochée | Les autres joueurs construisent dans la base ; ils paient l'or et les matériaux. Décochée : seul l'host construit. |
| Only the host manages the base | décochée | Ce que les autres demandent à la base (recherche, politiques, résidents, mode construction…) est refusé. |
| Duels between players | cochée | « Challenge to a duel » sur le personnage d'un autre joueur. Personne ne meurt, les deux sont soignés, rien n'est perdu. |
| Each player's auto combat protects its own | cochée | Le combat automatique sauve d'un coup mortel le joueur qui le lance et ses compagnons, avec la tactique et l'endurance de ce joueur. Décochée : celui de l'host protège tout le monde, celui d'un invité personne. |
| Players can kill each other | décochée | Décochée : un coup qui tuerait le personnage d'un autre joueur le laisse à 0 point de vie. |
| Combat on each player's time | cochée | Un monstre agit quand le joueur qu'il combat agit : personne n'attend. Passe avant le combat au tour par tour. |
| Each player on its own clock | cochée | Un invité marche et agit aussi régulièrement que l'host, pas au rythme du réseau. |
| Every player walks at the pace of a solo game | cochée | La durée d'un pas ne dépend plus de la vitesse des autres joueurs. |
| One date for the whole world | cochée | Le temps passé par un joueur seul sur une autre carte compte pour tous. Décochée : seule la date de l'host compte. |
| What time does to the world happens once | cochée | Météo, quêtes expirées, impôts, salaires et lettres sont faits par un seul jeu. |
| Everyone sleeps for themselves | cochée | Celui qui se couche dort tout de suite. La nuit passe pour le monde quand assez de joueurs dorment en même temps. |
| Night passes when | Everyone | Liste : *Everyone*, *Half* ou *One* des joueurs actifs endormis. |
| Away after | 5 min | Un joueur sans action depuis ce temps ne retient plus la nuit, la carte du monde ni la date (Off, 5, 15, 30 min). |
| Time stops when nobody plays | décochée | La date du monde attend tant qu'aucun joueur n'est connecté. |
| Temps sur la carte du monde | Horloge commune | Liste : *Horloge commune*, *Voyager ensemble* ou *Rythme personnel* (voir plus haut). |
| La base compte chaque joueur | cochée | Raids, mois de chance et jours avec le joueur comptent chaque joueur de la carte de la base. |
| Tonneaux et lait suivent le joueur | cochée | Étagères d'affinage, briques à sécher et lait maternel utilisent le joueur concerné. |
| Les invocations suivent l'invocateur | cochée | Une invocation reste avec son propre maître quand la carte change. |
| Tableaux de la ville pour chaque joueur présent | cochée | Palier de renommée et danger suivent les joueurs de la carte (demande les quêtes personnelles). |
| Stock des boutiques pour chaque joueur | cochée | Le bonus du meilleur joueur compte ; les étagères spéciales suivent celui qui ouvre la boutique. |
| Apparitions selon le joueur le plus proche | cochée | Les apparitions lisent les exploits du joueur le plus proche du point d'apparition. |
| Vie de la ville autour de chaque joueur | cochée | Habitants, rumeurs et pierres agissent autour de chaque joueur, selon son karma. |
| Tax on the most famous player | cochée | L'impôt du mois est calculé sur la plus haute renommée des joueurs connectés. Décochée : sur celle de l'host. |
| Auto-dump spares hand and tool belt | cochée | Le rangement automatique laisse ce que vous tenez et votre ceinture à outils. Décochée : comme le jeu. |
| Players come back by themselves after a connection loss | cochée | Un joueur qui perd la connexion revient tout seul dans la partie, pendant 3 minutes. |
| Repair the map by itself | cochée | Un joueur dont la carte ne correspond plus à celle de l'host la recharge, au plus une fois toutes les 30 secondes. |
| The world is saved by itself while others play | cochée | Toutes les 2 minutes tant qu'un autre joueur est dans la partie, et quand le dernier part. |
| The game opens to friends by itself when a world is loaded | cochée | Les amis peuvent rejoindre sans que l'host lance le serveur à la main. |
| The other players keep a copy of the world | décochée | Après chaque sauvegarde automatique, le PC de chaque joueur garde une copie du monde, hors de ses sauvegardes. |
| No world reload when the host and a player meet again (new) | décochée | Le joueur reste sur sa carte, ou ne charge que cette carte, au lieu du monde entier. |
| Show the mods of the game to the players | cochée | La liste des mods est connue avant de rejoindre, et un joueur refusé apprend quels mods diffèrent. |
| Require the same Elin version | décochée | Un joueur sur une autre version d'Elin est refusé. Décochée : il entre avec un avertissement. |
| Turn-Based Combat | cochée | Règle du mod d'origine. Sans effet tant que « Combat on each player's time » est cochée. |
| Shared Average Speed | décochée | Règle du mod d'origine : une seule vitesse pour tous les joueurs, la moyenne. |
| Players can be in teams | décochée | Les joueurs forment des équipes ; l'host garde dans sa sauvegarde qui est dans quelle équipe. Décochée : pas d'équipes, tout comme avant. |
| A base belongs to its team | décochée | Une base appartient à l'équipe qui l'a revendiquée : les autres équipes n'y ouvrent pas les coffres, ne ramassent pas, ne construisent pas, ne gèrent pas. |
| Joining a team needs its leader's approval | cochée | Un joueur qui demande à rejoindre une équipe attend l'accord de son chef. Décochée : il entre aussitôt. |
| A land deed for a new team | cochée | Le fondateur d'une nouvelle équipe reçoit un titre de propriété. Décochée : l'équipe l'achète au marchand. |
| Personal ownership inside a team | décochée | Un coffre, une porte ou un lit posé est à celui qui l'installe ; « Share with team » le rend à l'équipe. |
| Where a new player appears | start | Où apparaît un nouveau joueur : *start* (la prairie du début), *host* (la carte de l'host) ou un nom ou numéro de zone. |
| PvP between teams | décochée | Les joueurs d'équipes différentes sont ennemis hors des villes et de la zone d'arrivée. Pour mourir il faut encore « Players can kill each other ». |

### Joueur : l'onglet *Client Settings*

| Réglage | Au départ | Ce qu'il fait |
|---|---|---|
| Bind Ping Key | P | La touche qui montre un endroit de la carte aux autres. |
| Depot | vide | Où le monde commun est gardé : `github:proprietaire/depot`, `adresse:55557` ou un dossier. |
| Depot key | vide | Le mot de passe du serveur, ou la clé d'accès GitHub. Jamais réaffichée une fois saisie. |
| Fetch the mods of the game by itself | cochée | Les mods du Workshop qui manquent sont téléchargés et Elin se relance une fois avec les mods de la partie. |
| Keep the mods of the game (subscribe on the Workshop) | décochée | Votre compte Steam s'y abonne et ils restent allumés : plus de relance la fois suivante. |

Ces réglages sont dans `Elin\BepInEx\config\dk.elinplugins.elintogether.cfg`. Ce fichier contient la clé du dépôt
en clair : ne le partagez pas.

## Comment ça marche

### Le principe d'origine

Un seul jeu, celui de l'host, simule le monde. Chaque changement (un pas, un coup, un objet ramassé) en part sous
la forme d'un petit message, un **delta**, que les autres jeux appliquent. Les autres jeux envoient leurs actions
à l'host comme des demandes. L'host est la référence : quand deux jeux ne sont pas d'accord, c'est sa version qui
gagne.

Ce fork garde cela et ajoute ce qui suit.

### Quitter la carte de l'host : les baux de zone

Un joueur qui sort de la carte de l'host lui demande un **bail** sur la carte où il va. Avec le bail, son propre
jeu charge sa copie du monde et simule cette carte lui-même : monstres, temps, objets. Son lien avec l'host reste
ouvert pour le chat, les quêtes et la mémoire des dialogues.

- Chaque minute (un **point de sauvegarde**), et à son retour, il envoie la carte et son personnage à l'host, qui
  reste la référence. Un plantage perd ce qui s'est passé depuis le dernier point de sauvegarde.
- Une quête à donjon marche de la même façon, sur une zone neuve créée pour l'occasion et détruite ensuite.
- Chaque bail réserve une plage de numéros pour les quêtes créées sur cette carte : deux jeux ne donnent jamais
  le même numéro à deux quêtes différentes.

### Se retrouver sur une carte : les sessions de zone

Celui qui tient un bail peut ouvrir cette carte aux autres : il devient l'host **de cette carte**, et les autres
s'y connectent comme invités, en plus de leur lien avec le vrai host. Quand il part, le bail est **passé** à un
joueur qui reste, et les autres se reconnectent à celui-là. Le même mécanisme sert quand c'est l'host qui quitte
sa carte : le premier joueur resté reçoit le bail.

Quand un joueur entre sur une carte que quelqu'un d'autre tient, il reçoit cette carte de celui qui la tient. Sans
la case « No world reload when the host and a player meet again », il reçoit d'abord une copie fraîche du monde
entier : c'est pour cela que l'écran recharge.

### À chaque joueur le sien

- **Les compagnons** portent le numéro de leur joueur. Ils le suivent, voyagent avec lui et ne comptent que dans
  sa limite d'alliés.
- **Les quêtes aléatoires** : chaque jeu ne garde dans son journal que les quêtes aléatoires de son joueur. L'host
  conserve celles de tout le monde dans la sauvegarde, avec la renommée et le karma de chacun, et les redonne au
  joueur quand il arrive.
- **L'expédition** : chaque objet mis dans la caisse est marqué du numéro de celui qui l'a déposé. À 5 h, l'host
  vend tout, tient les comptes par joueur et envoie à chacun son argent, ou le lui garde s'il est absent.
- **Karma et crime** : le jeu retire du karma « au joueur » là où l'action se règle. Le mod retrouve le joueur
  derrière le tueur ou derrière la tâche et lui envoie la sanction. Quand un garde regarde quelqu'un, la question
  « le joueur est-il criminel ? » est posée pour le joueur regardé.
- **Combat et rythme** : un monstre lié à un joueur n'avance que quand ce joueur joue un tour. Chaque jeu tourne
  sur sa propre horloge, donc les pas d'un invité n'attendent pas le réseau.

### Un seul monde pour tous

- **Les quêtes d'histoire** : l'host tient le seul journal. Un dialogue joué par un invité se déroule dans le jeu
  de l'invité ; ce qu'il change dans le monde (une quête qui avance, un terrain revendiqué, un personnage qui
  rejoint) est envoyé à l'host, qui le fait pour tout le monde. Ce que le dialogue offre est créé par l'host aux
  pieds de l'invité, une seule fois.
- **La base** : recherche, compétences du foyer, politiques et réglages des résidents sont des demandes à l'host,
  qui vérifie, paie une fois et envoie le résultat à tous. En mode construction, le jeu de l'invité prépare la
  tâche (poser, miner, couper) et l'envoie au jeu qui tient la carte, qui la réalise avec l'or et les matériaux de
  l'invité. Le jeu qui simule une carte envoie, à la fin de chaque image, l'état de chaque case qui a changé.
- **Affinité, guildes, codex** : le jeu du joueur qui agit calcule, l'host garde la valeur et la renvoie à tous.
- **L'échange** : le jeu qui simule la carte tient la « table ». Il reçoit les intentions (inviter, accepter,
  offrir, confirmer), renvoie l'état aux deux joueurs et fait le transfert d'un seul coup, après avoir revérifié
  que chaque objet et chaque pièce existe encore.
- **Joueur contre joueur** : un coup qui tuerait le personnage d'un autre joueur le laisse à 0 point de vie, sauf
  en duel, où le combat s'arrête à ce plancher et où les deux sont soignés.

### Le temps

Il y a une seule date pour le monde : la plus avancée. Ce que le temps fait au monde (météo, impôts, salaires,
quêtes expirées) est fait une seule fois, par un seul jeu. Ce que le temps fait à un joueur (faim, nourriture qui
pourrit, délais de quête) est compté par joueur : le voyage ou la nuit d'un autre ne vous coûte rien. Celui qui se
couche dort sa propre nuit en quelques secondes ; la nuit du monde ne passe que si tous dorment en même temps.

### Rejoindre, et rester d'accord

1. **Poignée de main** : les deux jeux comparent la version du mod (obligatoirement la même), celle d'Elin (un
   avertissement) et la liste des mods.
2. **Personnage** : l'host dit quels personnages de ce monde sont les vôtres ; un nouveau joueur crée le sien.
3. **Copie du monde** : l'host envoie sa sauvegarde ; votre jeu la charge.
4. **Carte** : l'host envoie l'état de sa carte, puis la case où se tient votre personnage. Ce que les autres ont
   fait pendant votre chargement est gardé et rejoué ensuite.

Ensuite, toutes les 2 secondes, les jeux comparent quelques nombres par carte (combien d'objets, combien de
personnages, une somme de contrôle). Un écart qui dure est écrit dans les journaux, et la carte est rechargée sur
place, au plus une fois toutes les 30 secondes et jamais pendant un combat ou avec un menu ouvert. Un invité dont
le lien tombe retente la même partie toutes les 5 secondes pendant 3 minutes.

### Le dépôt

Un dépôt, c'est le monde en une seule archive (`world.zip`), plus un verrou qui dit qui héberge (`lock.json`).
L'host renouvelle le verrou chaque minute ; un verrou qui n'a pas été renouvelé depuis 3 minutes est libre. Avec
GitHub, chaque écriture nomme la version qu'elle remplace : de deux joueurs qui écrivent en même temps, GitHub n'en
accepte qu'un, et ce refus **est** le verrou. Rien n'est jamais forcé, et l'historique garde tous les mondes.

La sauvegarde d'un monde retient à qui est son personnage local. Chargée par un autre joueur qui a un personnage
dedans, les deux sont échangés avant que quoi que ce soit soit joué : celui qui prend le monde joue le sien,
l'ancien attend son joueur.

### Où sont les choses sur votre PC

| Quoi | Où |
|---|---|
| Le mod | `Elin\Package\Mod_ElinTogether` |
| Ses réglages | `Elin\BepInEx\config\dk.elinplugins.elintogether.cfg` |
| Ses journaux | `%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin\ElinMP\Logs\Session_<date>.log` |
| Le journal du jeu | `%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin\Player.log` |
| Les sauvegardes | `%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin\Save` |
| Le monde pris dans un dépôt | `…\Save\world_depot` (remplacé chaque fois que le monde est pris) |

## Limites connues

- **Un seul petit groupe y a joué pour de vrai.** Presque tout est vérifié par des tests en jeu sur un seul PC (deux
  ou trois fenêtres). « Pas joué » dans une note de version ou un message de commit veut dire exactement cela.
- **Une seule date pour le monde.** La façon dont un voyage sur la carte du monde la fait avancer dépend du choix
  *Temps sur la carte du monde* de l'host ; la nuit commune ne passe que si tout le monde dort.
- **Quand l'host part ou plante**, un invité peut reprendre le monde tout seul : la case existe (« Takeover »), elle
  est décochée tant que ce n'est pas joué entre deux vrais PC. Sans elle, quelqu'un prend le monde dans le dépôt.
- Retrouver l'host ne recharge plus tout le monde (case cochée d'office depuis la 0.26.691) ; au moindre doute le mod
  recharge comme avant.
- Un joueur refusé par celui qui tient une carte reçoit encore le monde entier.
- Sur l'écran de l'host, les invités qui marchent peuvent avancer par à-coups de trois cases.
- **Beaucoup de joueurs** : cinq fenêtres du jeu ont été testées sur un seul PC et jusqu'à 50 joueurs simulés (un
  outil de charge qui parle le protocole), jamais plus de trois vrais joueurs à la fois. Avec beaucoup de joueurs,
  quand tout le monde arrive en même temps l'host fait une pause d'environ une demi-seconde à une seconde par
  groupe.
- Les zones sont créées avec les dons de l'host (chances de monstres, de sanctuaires), et les habitants d'une base ne
  « remarquent » que lui (rumeurs, cadeaux).
- Quêtes à donjon à deux, quand c'est l'invité qui a la quête : « subjuguer », récolte et musique. La défense se
  fait seul. Un duel d'aventurier lancé par un invité laisse l'aventurier en double pendant le duel.
- Les duels entre joueurs n'ont ni arène ni pari.
- Mods : Elin se relance une fois à la première connexion (voir [Les parties avec mods](#les-parties-avec-mods)).
  Un mod retiré de la partie plus tard reste abonné chez un joueur qui a choisi de garder les mods. Pas essayé sur
  Steam Deck, Linux ni Mac.
- Rattrapage des mondes joués avant « une part pour chacun » : les recettes, les billets de la dette, les cadeaux du
  mariage et les cadeaux de dialogue ne sont pas redonnés.

La liste complète est gardée dans les notes du développeur, elle n'est pas publiée.

## Ce qu'il reste avant une vraie sortie

Ce fork est publié en préversions pour un groupe d'amis. Voici ce qui sépare cela d'une version qu'on pourrait
recommander à n'importe qui (état au 27 octobre 2026).

**1. La preuve sur de vrais PC**

- Presque tout est prouvé sur un seul PC, par le réseau local. Les salons Steam, les invitations, la latence réelle,
  trois ou quatre joueurs et une longue partie ne sont couverts que par quelques vraies soirées, et chacune a trouvé
  des défauts que les tests n'avaient pas vus. Il faut un petit groupe qui joue chaque version. En particulier :
  deux vrais PC avec deux comptes Steam.
- La reprise du monde par un invité (« Takeover ») : jouée à trois fenêtres, jamais entre deux PC.
- Un serveur public : les outils d'administration, le serveur dédié rejoint par Steam et la résurrection chez le
  barman ne sont testés que sur un seul PC.

**2. Des versions sûres**

- Une version a été publiée avec un défaut (0.26.691 : l'animal brossé par un invité allait à l'host) parce que
  toutes les séries de tests n'avaient pas été rejouées. La chaîne complète doit être verte avant chaque version, et
  quelques séries sont encore instables (nuit, invité).
- Un monde hébergé pour la première fois avec une nouvelle version est d'abord copié dans les sauvegardes de secours
  du jeu (depuis la version qui suit la 0.26.708). Lu dans une sauvegarde : rien du mod n'y est écrit que le jeu ne
  sache ignorer ; mais recharger ce monde dans un Elin sans le mod, ou avec le mod d'origine, n'a jamais été joué.

**3. L'installation**

- Le mod demande le canal **Nightly** d'Elin, et chaque version est compilée pour une version précise du jeu.
- Un zip (`Install.bat`, `Uninstall.bat`, `Server.bat`, `README.txt`), en anglais, qui remplace la version Workshop du mod d'origine. Le mainteneur prépare une page Workshop ; aucune date n'est promise.
- Tous les joueurs doivent avoir exactement la même version.

**4. Ce qui manque**

- Mods : la liste de l'host appliquée sans relance ; une partie dont la liste change.
- Duels : arène, pari. Quêtes de défense à deux.
- Une horloge « monde vivant » en temps réel (la date du monde qui avance même quand personne ne dort ni ne
  voyage) : décidée, pas construite.
- Équipes : recettes, codex, impôts fonciers et journal d'histoire propres à chaque équipe, puis la quête
  principale par équipe : décidés, pas construits.
- Le dépôt en ligne du monde : la reprise automatique depuis le dépôt.

**5. Avant de le recommander**

- Les README chinois et japonais sont plus courts que celui-ci et n'ont pas été relus par quelqu'un dont c'est la
  langue ; les réglages du mod sont en anglais.
- Décider ce qu'on fait avec le projet d'origine : ce fork change le cœur du mod et n'est pas fait pour y être
  fusionné tel quel.
- Pour signaler un défaut : le bouton « Prepare a bug report » de l'onglet *Client Settings* écrit sur le bureau un
  zip avec les journaux du jeu et du mod (ni sauvegarde, ni fichier de réglages).

## Comment c'est testé

Un changement est censé venir avec un test en jeu, cité dans le message de son commit ; quand il a été publié sans,
le commit et la note de version disent « pas joué ». Les tests pilotent de vraies fenêtres du jeu par un pont de
test (versions Debug seulement) : une fenêtre host et une à quatre fenêtres invitées sur un seul PC.

- **Des suites par sujet** : voyage, cartes partagées, quêtes, échange, base, construction, duels, sommeil, mort,
  dépôt, mods… Chacune écrit ce qu'elle vérifie et ce qu'elle ne joue **pas** comme un joueur.
- **Une première partie** : deux joueurs qui jouent ensemble pour la première fois sur un monde tout neuf, par
  les écrans du jeu : création du personnage, texte d'ouverture, acte de propriété, ouverture de la partie, arrivée
  de l'invité, cadeaux d'Ashland, puis sauvegarde, fermeture des deux jeux, relance et retour.
- **Une chasse aux dialogues** : chaque joueur marche jusqu'à chaque personnage de la carte et essaie chaque choix
  de ses menus ; une boucle, une erreur du jeu, deux jeux qui ne sont plus d'accord ensuite ou un objet en double
  est un défaut.
- **Le bot** : joue au hasard sur une fenêtre et vérifie que les jeux restent d'accord.

Le banc de test n'est pas publié.

Ce que le banc ne peut pas prouver : Steam entre deux vrais PC, une vraie souris, et tout ce que personne n'a pensé
à tester. Ce sont les vraies soirées qui l'ont trouvé. La règle du projet : lire les journaux avant de conclure,
et dire ce qui n'a pas été joué.

## En cas de problème

- L'host décoche la case en cause : sur ce point, le mod se comporte de nouveau comme l'original.
- Un joueur dont le jeu ne correspond plus à celui des autres tape `emp.reconnect_self` dans la console.
- Elin s'est fermé tout seul à la première connexion : c'est la relance pour les mods, voir
  [Les parties avec mods](#les-parties-avec-mods).
- Signalez-le dans [les tickets de ce dépôt](https://github.com/jeanfreger/elin-together-independence/issues), avec les
  fichiers `Player.log` et `ElinMP/Logs/Session_<date>.log` de chaque joueur
  (`%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin`). Merci de ne pas signaler au projet d'origine les problèmes
  de ce fork.

## Compiler

Le mod se compile avec `dotnet build ./ElinTogether -c ReleaseNightly`. Le jeu doit être installé : la variable
d'environnement `ElinGamePath` doit donner son dossier. `SteamContentPath`
(`steamapps/workshop/content`) donne `YKFramework.dll`. SDK .NET 11.0 preview, voir `global.json`.

```ps
git clone https://github.com/jeanfreger/elin-together-independence
cd elin-together-independence
dotnet restore ./ElinTogether --locked-mode
dotnet build ./ElinTogether -c ReleaseNightly
```

La fabrication du zip d'une version et les outils de test sont gardés hors du dépôt.

## Crédits

Le mod est l'œuvre de l'équipe Elin Together : [DK](https://github.com/gottyduke) et
[Redgeioz](https://github.com/Redgeioz) (code, architecture), [105gun](https://github.com/105gun) (code),
[Han](https://github.com/chuahan), Omega, [InuiDame](https://github.com/InuiDame),
[Drakeny](https://github.com/Drakeny) (tests), noa (Elin). Licence MIT, voir [LICENSE](LICENSE).

Tests de cette version : lemiwinks, nardole, 梦天冰, fartcreature.

Fait par jeanfreger avec Claude (Anthropic). Les changements de ce fork ont été écrits avec un assistant de programmation (Claude Code), dirigé par le
propriétaire du fork. Aucun fichier du jeu ni code décompilé du jeu n'est dans ce dépôt.
