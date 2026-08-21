# 🐱 NyaLauncher

> Un launcher Minecraft moderne, multiplateforme et léger, né pour la liberté.
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)
![License](img/badges/license.svg)

---

## ✨ Présentation

**NyaLauncher** est un launcher Minecraft multiplateforme développé avec **Avalonia UI 12.1.1** et **.NET 10**.<br>
Non seulement léger et rapide, il met surtout l'accent sur la **protection de la vie privée** et la **personnalisation de l'interface**, afin que vous gardiez un contrôle total tout en profitant du jeu.<br>
NyaLauncher est un logiciel libre. Hormis les bibliothèques conservées lorsque cela est nécessaire, tout le code est publié sous [Apache License 2.0](LICENSE).<br>
Le launcher n'effectue aucune télémétrie à votre insu, ne viole en rien votre vie privée et n'impose aucune limitation de fonctionnalités.

---

## 📦 Pile technologique

| Composant                        | Technologie                          |
|----------------------------------|--------------------------------------|
| Framework UI                     | Avalonia UI 12.1.1                   |
| Runtime (environnement d'exécution) | .NET 10                          |
| Contrat d'extension de composants | .NET 10, sans dépendance à Avalonia |

---

## 🔧 Structure du projet

| Projet                               | Rôles                                                                    |
|--------------------------------------|--------------------------------------------------------------------------|
| NyaLauncher.Core                     | 🐱 Ensemble des fonctions de lancement du noyau de NyaLauncher            |
| NyaLauncher.Avalonia                 | Interface frontale de NyaLauncher, basée sur Avalonia                    |
| NyaLauncher.Avalonia.Animations      | Bibliothèque d'animations de l'interface frontale de NyaLauncher         |
| NyaLauncher.Plugin.Abstractions      | Contrats de composants indépendants du framework UI, géométrie, éléments, état d'exécution et validation |
| NyaLauncher.MinecraftTokenCrypto     | (**Bibliothèque fermée, car l'algorithme ne se prête pas à une divulgation publique**) Algorithme/stockage de chiffrement des jetons de connexion des comptes Minecraft premium |

---

## 🔃 Plan de mise à jour

### 📝 Règles de nommage des versions
| Phase           | Signification                                                              |
|-----------------|----------------------------------------------------------------------------|
| beta            | Phase d'écriture du launcher, totalement inutilisable                      |
| preview         | Phase de test, partiellement utilisable mais déconseillée pour un usage quotidien (phase actuelle de 0.1.0preview-3) |
| release         | Version officielle, entièrement utilisable                                 |
| gp (spécial)    | Numéro de version spécifique à la branche newgui, correspondant au preview de la branche principale |

### Fonctionnalités prévues
- Fonctionnalité de plugins (déjà validée avec succès sur la branche en aval testplug)
- Thèmes personnalisés (prévus pour la prochaine version preview)
- Multilingue (date encore à définir)
- Traduction/vérification assistée par IA (inconnu)
- Multijoueur en ligne (???)

---

## 🛠️ Démarrage rapide

### 🪟🍎🐧 Configuration requise

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) ou version ultérieure
- Windows 10+, macOS Ventura+, Linux Kernel 5.0+
- Runtime de bureau (Windows/macOS/Linux)
> Le portage vers HarmonyOS est encore à l'étude.

### 🔧 Clonage et compilation

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📈 Journal des modifications
Modifications récentes

v0.1.0-preview3
- Fonctionnalités newgui finalisées et fusionnées dans la branche main
- Ajout d'un grand nombre de composants
- Petite refonte du module Core (progression : 25/100 %)
- Ajout de fonctions de téléchargement liées à Minecraft
- Ajout d'une fonction de journalisation pour conserver les fichiers de données générés au runtime par le launcher/le jeu
- Correction des problèmes de style codés en dur dans le frontend et petites optimisations diverses
- Le système de plugins est en cours de test ; la première API utilisable arrive bientôt
- Code redondant/mort du backend supprimé
- Partie du module animations supprimée ; refonte à venir
- Règles de nommage des versions modifiées
- Herobrine supprimé
![Capture d'écran de la fenêtre principale v0.1.0-preview3](img/v0.1.0preview-3-mainwindow.png)
![Capture d'écran de la gestion du jeu v0.1.0-preview3](img/v0.1.0preview-3-game.png)
![Capture d'écran de la gestion des comptes v0.1.0-preview3](img/v0.1.0preview-3-account.png)

v0.1.0-gp2 (branche newgui)

> `v0.1.0-gp2` ne désigne que la deuxième itération de l'interface de v0.1.0 newgui et n'est pas écrite dans le numéro de version de Core.<br>Cette version n'est pas liée à la branche main.

- Refonte de l'interface (sur la branche newgui) : la page d'accueil est devenue des blocs de composants modifiables, offrant plus de liberté de personnalisation (pas encore abouti ; l'ancienne interface est conservée dans la branche main)
- Ajout du lancement hors ligne et du lancement premium (en ligne)
- Correction d'un bug dans readme.md (?)
- Ajout de la gestion multi-comptes
- Ajout de l'enregistrement de la configuration : après une configuration, elle est enfin conservée 😭
- Optimisation de la recherche de Java, corrigeant le problème où Java pouvait démarrer mais ne pouvait pas être utilisé
- Herobrine supprimé
![Capture d'écran de la fenêtre principale v0.1.0-gp2](img/v0.1.0pre2-mainwindow.png)
![Capture d'écran de l'écran de lancement v0.1.0-gp2](img/v0.1.0pre2.png)
![Capture d'écran des paramètres v0.1.0-gp2](img/v0.1.0pre2-settings.png)
![Capture d'écran de la personnalisation v0.1.0-gp2](img/v0.1.0pre2-settings2.png)

v0.1.0-pre1
- Extraction de l'interface graphique de l'UI dans une bibliothèque séparée (NyaLauncher.Avalonia.Animations)
- Amélioration de certains problèmes de saccades visuelles
- Herobrine supprimé
![Capture d'écran de la fenêtre principale v0.1.0-pre1](img/v0.1.0pre1.png)