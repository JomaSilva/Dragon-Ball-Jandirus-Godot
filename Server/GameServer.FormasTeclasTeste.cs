using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--formasteclasteste` -- O C DO KI DIVINO E A TECLA QUE VAI DIRETO (o adendo do dono, 2026-10-23):
/// *"atualmente quem libera god ki nao consegue virar as formas mortais, so as divinas, coloque q as transformacoes divinas podem ser
/// colocadas em teclas assim indo direto pra elas, ou tendo um botao q troca entre o c duas vezes virar as formas mortais e o c duas vezes
/// virar forma divina, outra coisa formas com acima de 50% de maestria q estao colocadas em teclas, podem sim pular etapas anteriores"*.
/// As decisoes dele: "as duas" (o botao gravado no personagem E a divina na tecla indo direto) e a maestria "da propria forma da tecla".
///
/// As REGRAS moram no Core (`EstadoDeForma.CPelasDivinas` no `Proxima`, `EstadoDeForma.VaiDiretoPelaTecla` no `Avaliar` com `pelaTecla`) e
/// a `pformas` (secao [5b]) as mede na conta pura. Esta mede a CADEIA, que e onde regra escrita e nunca ligada se esconde: um cliente cru
/// SAIYAJIN entra pela porta e tudo vai PELO FIO -- o `C2S.Verbo` do botao (`divinas_c`) e da tecla de forma (`forma <id>`), o
/// `C2S.Transformar` do C duplo e do X --, com o disco de verdade e o relog pela porta:
///   F1. O BOTAO: o lado de fabrica do personagem novo, as duas posicoes com a frase, o .json, o relog que a traz de volta (e o C que a
///       obedece depois dele) e o save de antes do botao.
///   F2. O C: as divinas primeiro (o SSG, e nao o Blue direto), as mortais primeiro (o Super Saiyajin -- o defeito do dono) e o tronco
///       mortal inteiro, a outra familia de reserva, e o C que NAO pula degrau nem com 50% de maestria. Contra-exemplo `CSurdoAFamiliaDeTeste`.
///   F3. A DIVINA NA TECLA: o Blue da base e o SSG de dentro do Super Saiyajin, a porta do ki divino que continua e a forma nunca assumida
///       que nao vai. Contra-exemplo `DivinaPedeOCaminhoDeTeste`.
///   F4. A MORTAL COM 50% NA TECLA: o SSJ3 da base, os 49% que nao pulam (com a frase do quanto falta), as portas que continuam (a maestria
///       do SSJ2, o poder, o Ki), o X de sempre depois do salto e a forma nunca assumida. Contra-exemplo `MaestriaNaoPulaDeTeste`.
///
/// Cada afirmacao principal tem o CODIGO dela no comeco do rotulo ("F2b.", ...): e por ele que a contraprova por mutacao (o `ciclo.py` do
/// agente `formasteclas`) sabe onde cada linha tirada da regra tem de reprovar.
///
///     Godot --headless --server --port 10880 --formasteclasteste
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDasTeclas = "bancada_fteclas";

	/// <summary>As formas que o corpo da bancada ja assumiu (liberadas, e com a estreia vista: nenhuma cinematica longa no meio da medida).</summary>
	private static readonly string[] FormasDaBancadaDasTeclas = ["ssj1", "ssj2", "ssj3", "ssg", "blue"];

	private bool _ftRodando, _ftJaRodou;
	private int _ftOk, _ftFalhou;

	private void AfirmarFt(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ftOk++; GD.Print($"[formasteclas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _ftFalhou++; GD.PrintErr($"[formasteclas]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasTeclasDeForma(int porta)
	{
		if (_ftRodando || _ftJaRodou) return;
		_ftRodando = _ftJaRodou = true;
		_ftOk = _ftFalhou = 0;
		GD.Print("[formasteclas] ================ O C DO KI DIVINO E A TECLA QUE VAI DIRETO (dono, 2026-10-23) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = FtEntrar(mesa, cli);
			AfirmarFt("(montagem) o cliente entra pela porta como SAIYAJIN, e e jogador", pl != null && EhJogador(pl) && pl.Race == "Saiyan",
					  pl == null ? $"recusa: {cli.Recusa}" : pl.Race);
			if (pl == null) return;

			pl = OBotaoDoC(mesa, cli, pl);
			if (pl == null) return;
			OCPeloFio(mesa, cli, pl);
			ADivinaNaTecla(mesa, cli, pl);
			AMortalComMetadeNaTecla(mesa, cli, pl);
		}
		catch (Exception e)
		{
			AfirmarFt("a bancada correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			EstadoDeForma.CSurdoAFamiliaDeTeste = EstadoDeForma.DivinaPedeOCaminhoDeTeste = false;
			EstadoDeForma.MaestriaNaoPulaDeTeste = EstadoDeForma.PulaFormaNuncaUsadaDeTeste = false;
			EscutaDeAvisos = null;
			EscutaDeAnuncios = null;
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(ContaDaBancadaDasTeclas) + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[formasteclas] nao apaguei '{ContaDaBancadaDasTeclas}': {e.Message}"); }
			_ftRodando = false;
			GD.Print($"[formasteclas] ================ {_ftOk} OK, {_ftFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// F1) O BOTAO: A ESCOLHA, A FRASE, O DISCO E O RELOG
	// =====================================================================
	/// <summary>Devolve o corpo de DEPOIS dos relogs (o `ServerPlayer` e outro a cada entrada), ou nulo se a porta nao deixou voltar.</summary>
	private ServerPlayer? OBotaoDoC(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl)
	{
		GD.Print("[formasteclas] -- F1) o botao: as duas posicoes, a frase, o disco e o relog --");
		AfirmarFt("F1a. o personagem NOVO entra com o lado de fabrica do botao: as formas DIVINAS primeiro (o que o C ja fazia)",
				  pl.Forma.CPelasDivinas == true, FtTxt(pl.Forma.CPelasDivinas));
		FtMontar(pl);

		List<string> ditos = FtVerbo(mesa, cli, pl, "divinas_c");
		AfirmarFt("F1b. o verb `divinas_c` pelo fio troca o C pras formas MORTAIS -- e o servidor diz em que pe ficou",
				  pl.Forma.CPelasDivinas == false && FtDito(ditos, "MORTAIS"), $"{FtTxt(pl.Forma.CPelasDivinas)} | {FtJuntos(ditos)}");
		ditos = FtVerbo(mesa, cli, pl, "divinas_c");
		AfirmarFt("F1c. ...e o segundo aperto volta pras DIVINAS, dizendo tambem",
				  pl.Forma.CPelasDivinas == true && FtDito(ditos, "DIVINAS"), $"{FtTxt(pl.Forma.CPelasDivinas)} | {FtJuntos(ditos)}");

		FtVerbo(mesa, cli, pl, "divinas_c");   // as mortais de novo: e o lado que se distingue do de fabrica no disco
		bool? noDisco = mesa.NoDisco(ContaDaBancadaDasTeclas)?.CPelasDivinas;
		AfirmarFt("F1d. a escolha vai pro DISCO na hora: o .json da conta guarda `false` (as mortais)", noDisco == false, FtTxt(noDisco));

		// O RELOG PELA PORTA: o corpo e outro, montado pelo login de producao a partir do .json.
		if (FtRelogar(mesa, cli) is not { } volta) { AfirmarFt("(montagem) o cliente sai e volta pela porta", false, $"recusa: {cli.Recusa}"); return null; }
		pl = volta;
		AfirmarFt("F1e. o RELOG traz a escolha de volta do disco: o corpo novo entra com as MORTAIS primeiro",
				  pl.Forma.CPelasDivinas == false, FtTxt(pl.Forma.CPelasDivinas));
		FtMontar(pl);
		ditos = FtC(mesa, cli, pl);
		AfirmarFt("F1f. ...e a escolha VALE depois do relog: o C pelo fio, da base, vai pro Super Saiyajin",
				  pl.Forma.Atual == "ssj1", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		FtABase(pl);

		// O SAVE DE ANTES DO BOTAO: o campo sai do .json (com o jogador fora -- logado, a conta e a da memoria) e a porta o le de novo.
		// A virgula sai junto com o campo, como na `GradesTeste`: um .json invalido mediria sintaxe, e nao migracao.
		if (!mesa.SaiDoJogo(cli, ContaDaBancadaDasTeclas) || _store == null)
		{
			AfirmarFt("(montagem) o cliente saiu do jogo pra o .json ser editado", false, "");
			return null;
		}
		string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(ContaDaBancadaDasTeclas) + ".json");
		string texto = System.IO.File.ReadAllText(caminho);
		string semCampo = System.Text.RegularExpressions.Regex.Replace(
			texto, ",?\\s*\"CPelasDivinas\"\\s*:\\s*(true|false|null)\\s*,?", m =>
				m.Value.TrimStart().StartsWith(',') && m.Value.TrimEnd().EndsWith(',') ? "," : "");
		AfirmarFt("(montagem) o campo `CPelasDivinas` existia no .json e foi tirado",
				  semCampo.Length < texto.Length && !semCampo.Contains("CPelasDivinas", StringComparison.Ordinal),
				  $"{texto.Length} -> {semCampo.Length} bytes");
		System.IO.File.WriteAllText(caminho, semCampo);
		if (FtEntrar(mesa, cli) is not { } antigo) { AfirmarFt("(montagem) o cliente volta pela porta com o .json antigo", false, $"recusa: {cli.Recusa}"); return null; }
		pl = antigo;
		AfirmarFt("F1g. um save de ANTES do botao (o campo fora do .json) entra com as DIVINAS primeiro -- o lado de fabrica, e nao as mortais",
				  pl.Forma.CPelasDivinas == true, FtTxt(pl.Forma.CPelasDivinas));
		FtMontar(pl);
		return pl;
	}

	// =====================================================================
	// F2) O C DUPLO, PELO FIO
	// =====================================================================
	private void OCPeloFio(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl)
	{
		GD.Print("[formasteclas] -- F2) o C duplo pelo fio, com o ki divino desperto --");

		FtABase(pl);
		pl.Forma.CPelasDivinas = true;
		List<string> ditos = FtC(mesa, cli, pl);
		AfirmarFt("F2a. (controle) as DIVINAS primeiro: o C pelo fio, da base, vai pro Super Saiyajin God -- e NAO direto pro Blue (so a tecla pula)",
				  pl.Forma.Atual == "ssg", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		FtABase(pl);
		pl.Forma.CPelasDivinas = false;
		ditos = FtC(mesa, cli, pl);
		AfirmarFt("F2b. **as MORTAIS primeiro: o C pelo fio, da base, vai pro Super Saiyajin** -- o defeito do dono, consertado",
				  pl.Forma.Atual == "ssj1", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		FtEm(pl, "ssj1");
		FtC(mesa, cli, pl);
		string doSsj1 = pl.Forma.Atual;
		FtC(mesa, cli, pl);
		string doSsj2 = pl.Forma.Atual;
		AfirmarFt("F2c. ...e o C sobe o tronco mortal inteiro com o ki divino desperto: do Super Saiyajin pro 2, e do 2 pro 3",
				  doSsj1 == "ssj2" && doSsj2 == "ssj3", $"ssj1 -> {doSsj1} -> {doSsj2}");

		FtABase(pl);
		EstadoDeForma.CSurdoAFamiliaDeTeste = true;
		try
		{
			ditos = FtC(mesa, cli, pl);
			AfirmarFt("(defeito injetado: o C surdo a familia -- o jogo de antes) a mesma regua da F2b REPROVA: as mortais primeiro, e da "
					  + "base o C vai pro Super Saiyajin God", pl.Forma.Atual == "ssg", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { EstadoDeForma.CSurdoAFamiliaDeTeste = false; }

		// A RESERVA: a familia escolhida sem degrau acima, e o C sobe pela outra. O `Limiares = null` poe a porta na constante do catalogo
		// (o limiar sorteado no nascimento poderia estar abaixo do BP escrito aqui, e a regua mediria "nao ha recusa").
		FtEm(pl, "ssj1");
		LimiaresPessoais? limiares = pl.Forma.Limiares;
		double bp = pl.Ficha.BP;
		pl.Forma.Limiares = null;
		pl.Ficha.BP = Catalogo.PortaSsj2 - 1;
		try
		{
			ditos = FtC(mesa, cli, pl);
			AfirmarFt("F2d. a outra familia e a RESERVA: as mortais primeiro, no Super Saiyajin com o 2 fora do alcance por poder, o C vai pro Blue",
					  pl.Forma.Atual == "blue", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally
		{
			pl.Forma.Limiares = limiares;
			pl.Ficha.BP = bp;
		}

		// SO A TECLA PULA: o mesmo corpo com o SSJ3 a 50% -- o C sobe um degrau.
		FtABase(pl);
		pl.Forma.Maestria.Por("ssj3", EstadoDeForma.MaestriaQuePulaDegrau);
		try
		{
			ditos = FtC(mesa, cli, pl);
			AfirmarFt($"F2e. SO A TECLA PULA: com o SSJ3 a {EstadoDeForma.MaestriaQuePulaDegrau:0}% e as mortais primeiro, o C pelo fio sobe UM "
					  + "degrau -- da base pro Super Saiyajin, e nao pro 3", pl.Forma.Atual == "ssj1", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { pl.Forma.Maestria.Por("ssj3", 0); }
		FtABase(pl);
	}

	// =====================================================================
	// F3) A FORMA DIVINA NA TECLA VAI DIRETO
	// =====================================================================
	private void ADivinaNaTecla(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl)
	{
		GD.Print("[formasteclas] -- F3) a forma divina na tecla vai direto --");

		// AS MORTAIS PRIMEIRO de proposito: a tecla nao e o C, e a familia do botao nao muda o que ela faz.
		pl.Forma.CPelasDivinas = false;
		FtABase(pl);
		double buffAntes = pl.Ficha.ssjBuff;
		EscutaDeAnuncios = [];
		List<string> ditos = FtTecla(mesa, cli, pl, "blue");
		var anuncios = EscutaDeAnuncios ?? [];
		EscutaDeAnuncios = null;
		AfirmarFt("F3a. **a tecla do Blue (`forma blue` pelo fio) sai da BASE direto pro Blue** -- com o multiplicador dele e o anuncio pra zona",
				  pl.Forma.Atual == "blue" && pl.Ficha.ssjBuff > buffAntes + 1e-9 && anuncios.Any(a => a.Quem == pl.Id && a.Para == "blue"),
				  $"{pl.Forma.Atual}, x{buffAntes:0.##} -> x{pl.Ficha.ssjBuff:0.##}, {anuncios.Count} anuncio(s) | {FtJuntos(ditos)}");

		FtEm(pl, "ssj1");
		ditos = FtTecla(mesa, cli, pl, "ssg");
		AfirmarFt("F3b. **a tecla do SSG sai de DENTRO do Super Saiyajin** (o exemplo do dono)",
				  pl.Forma.Atual == "ssg", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		FtABase(pl);
		EstadoDeForma.DivinaPedeOCaminhoDeTeste = true;
		try
		{
			ditos = FtTecla(mesa, cli, pl, "blue");
			AfirmarFt("(defeito injetado: a forma divina pede o caminho -- o jogo de antes) a mesma regua da F3a REPROVA: a tecla do Blue, da "
					  + "base, nao transforma", pl.Forma.NaBase, $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { EstadoDeForma.DivinaPedeOCaminhoDeTeste = false; }

		FtABase(pl);
		pl.Ficha.godki!.mastery = 20;
		try
		{
			ditos = FtTecla(mesa, cli, pl, "blue");
			AfirmarFt($"F3c. a porta do KI DIVINO continua: com 20% de maestria divina a tecla do Blue e recusada -- e a frase diz os "
					  + $"{Catalogo.GodkiBluePct:0}%", pl.Forma.NaBase && FtDito(ditos, $"{Catalogo.GodkiBluePct:0}%"), $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { pl.Ficha.godki.mastery = 40; }

		FtABase(pl);
		pl.Forma.Liberadas.Remove(Catalogo.Rede("blue"));
		try
		{
			ditos = FtTecla(mesa, cli, pl, "blue");
			AfirmarFt("F3d. a forma divina que o corpo NUNCA assumiu nao vai direto: sem o Blue nas liberadas, a tecla da base e recusada (a "
					  + "tecla so lista forma ja usada, e o servidor confere)", pl.Forma.NaBase, $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { pl.Forma.Liberar("blue"); }
		FtABase(pl);
	}

	// =====================================================================
	// F4) A FORMA COM 50% DE MAESTRIA NA TECLA PULA OS DEGRAUS DE ANTES
	// =====================================================================
	private void AMortalComMetadeNaTecla(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl)
	{
		GD.Print("[formasteclas] -- F4) a forma com 50% de maestria nela pula os degraus de antes --");
		double pula = EstadoDeForma.MaestriaQuePulaDegrau;
		pl.Forma.CPelasDivinas = false;

		FtABase(pl);
		pl.Forma.Maestria.Por("ssj3", pula);
		List<string> ditos = FtTecla(mesa, cli, pl, "ssj3");
		AfirmarFt($"F4a. **a tecla do SSJ3 com {pula:0}% de maestria nele sai da BASE direto pro SSJ3** (o exemplo do dono)",
				  pl.Forma.Atual == "ssj3", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		FtABase(pl);
		pl.Forma.Maestria.Por("ssj3", pula - 1);
		ditos = FtTecla(mesa, cli, pl, "ssj3");
		AfirmarFt($"F4b. com {pula - 1:0}% a tecla da base e recusada -- e a frase diz que ele vem depois do 2 e quanto falta "
				  + $"({pula:0}%, e o corpo tem {pula - 1:0}%)",
				  pl.Forma.NaBase && FtDito(ditos, "vem depois") && FtDito(ditos, $"{pula:0}%") && FtDito(ditos, $"{pula - 1:0}%"),
				  $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		pl.Forma.Maestria.Por("ssj3", pula);
		FtABase(pl);
		EstadoDeForma.MaestriaNaoPulaDeTeste = true;
		try
		{
			ditos = FtTecla(mesa, cli, pl, "ssj3");
			AfirmarFt("(defeito injetado: a maestria nao pula -- o jogo de antes) a mesma regua da F4a REPROVA: com 50%, a tecla do SSJ3 da "
					  + "base nao transforma", pl.Forma.NaBase, $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { EstadoDeForma.MaestriaNaoPulaDeTeste = false; }

		// AS OUTRAS PORTAS CONTINUAM -- o salto e so do caminho.
		FtABase(pl);
		pl.Forma.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct - 1);
		try
		{
			ditos = FtTecla(mesa, cli, pl, "ssj3");
			AfirmarFt("F4c. a porta PROPRIA do SSJ3 continua: com 49% no SSJ2 a tecla recusa pela maestria do SSJ2",
					  pl.Forma.NaBase && FtDito(ditos, "de maestria em Super Saiyajin 2"), $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally { pl.Forma.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct); }

		FtABase(pl);
		LimiaresPessoais? limiares = pl.Forma.Limiares;
		double bp = pl.Ficha.BP;
		pl.Forma.Limiares = null;
		pl.Ficha.BP = Catalogo.PortaSsj3 - 1;
		try
		{
			ditos = FtTecla(mesa, cli, pl, "ssj3");
			AfirmarFt("F4d. a porta de PODER continua: abaixo da porta do SSJ3 a tecla recusa (\"alem do seu alcance\")",
					  pl.Forma.NaBase && FtDito(ditos, "alem do seu alcance"), $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
		}
		finally
		{
			pl.Forma.Limiares = limiares;
			pl.Ficha.BP = bp;
		}

		FtABase(pl);
		ditos = FtTecla(mesa, cli, pl, "ssj3", ki: 0.05);
		AfirmarFt("F4e. o Ki minimo continua: com 5% de Ki a tecla recusa (\"Ki baixo demais\")",
				  pl.Forma.NaBase && FtDito(ditos, "Ki baixo demais"), $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		// O X DEPOIS DO SALTO: o salto e um `EntrarNaForma` seco, entao o corpo no SSJ3 vindo da base e este -- e o X o recua pelo
		// `EstagioAnterior`, pro degrau que ele ja viu.
		FtEm(pl, "ssj3");
		ditos = FtC(mesa, cli, pl, subir: false);
		AfirmarFt("F4f. (controle) o X depois do salto e o de sempre: do SSJ3 ele recua UM degrau, pro SSJ2 que o corpo ja viu",
				  pl.Forma.Atual == "ssj2", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

		FtABase(pl);
		pl.Forma.Liberadas.Remove(Catalogo.Rede("ssj3"));
		pl.Forma.Maestria.Por("ssj3", 60);
		try
		{
			ditos = FtTecla(mesa, cli, pl, "ssj3");
			AfirmarFt("F4g. a forma que o corpo NUNCA assumiu nao pula: sem o SSJ3 nas liberadas, com 60% na barra, a tecla da base e "
					  + "recusada -- e a frase diz por que", pl.Forma.NaBase && FtDito(ditos, "já assumiu"), $"{pl.Forma.Atual} | {FtJuntos(ditos)}");

			FtABase(pl);
			EstadoDeForma.PulaFormaNuncaUsadaDeTeste = true;
			try
			{
				ditos = FtTecla(mesa, cli, pl, "ssj3");
				AfirmarFt("(defeito injetado: a tecla pula tambem a forma nunca assumida) a mesma regua da F4g REPROVA: o SSJ3 que o corpo "
						  + "nunca assumiu entra pela tecla da base", pl.Forma.Atual == "ssj3", $"{pl.Forma.Atual} | {FtJuntos(ditos)}");
			}
			finally { EstadoDeForma.PulaFormaNuncaUsadaDeTeste = false; }
		}
		finally
		{
			pl.Forma.Liberar("ssj3");
			pl.Forma.Maestria.Por("ssj3", 0);
		}
		FtABase(pl);
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// Do aperto de mao ao corpo no mundo, como SAIYAJIN de linhagem comum -- o caminho da `MesaDeClientesCrus.EntraNoMundo`, com a ficha
	/// de criacao trocada (a dela e Humana). Na volta (o slot ja ocupado) escolhe o personagem que esta no disco.
	/// </summary>
	private ServerPlayer? FtEntrar(MesaDeClientesCrus mesa, ClienteCruDeTeste cli)
	{
		if (!mesa.Entra(cli) || !mesa.Loga(cli, ContaDaBancadaDasTeclas) || cli.Slots is not { Count: > 0 } slots) return null;
		if (slots[0].Ocupado) cli.EscolherSlot(0);
		else
		{
			var ficha = new CharacterDraft
			{
				Name = "Tecla", Race = "Saiyan", Planet = "Vegeta", Gender = "Male", ChosenClass = "Saiyan",
				Backstory = "Personagem de bancada: um Saiyajin que despertou o ki divino, pra medir o C duplo e as teclas de forma.",
			};
			ficha.Age = Math.Min(18, ficha.IdadeMaxima);
			cli.CriarPersonagem(0, ficha);
		}
		return mesa.Bombeia(() => cli.NoMundo || cli.Recusa != null, 8000) && cli.NoMundo ? mesa.CorpoDe(ContaDaBancadaDasTeclas) : null;
	}

	/// <summary>Sai pela porta (o `Drop` de producao grava) e volta: o corpo novo e montado pelo login a partir do .json.</summary>
	private ServerPlayer? FtRelogar(MesaDeClientesCrus mesa, ClienteCruDeTeste cli) =>
		mesa.SaiDoJogo(cli, ContaDaBancadaDasTeclas) ? FtEntrar(mesa, cli) : null;

	/// <summary>
	/// O CORPO DA MEDIDA: Saiyajin de classe Normal (a classe se sorteia no nascimento, e Lendario/Primal trocariam a escada), o ki divino
	/// desperto a 40% (o Blue abre aos 33), poder de sobra, as cinco formas ja assumidas (liberadas e com a estreia vista) e a maestria que o
	/// SSJ3 cobra do SSJ2. OS GRAUS FORA DO CAMINHO: aqui o assunto e a familia; o encontro dela com os graus e da `pformas` [5b]. A familia
	/// do C NAO e tocada aqui -- e ela que o login e o botao escrevem.
	/// </summary>
	private void FtMontar(ServerPlayer pl)
	{
		pl.Class = pl.Ficha.Class = "Normal";
		if (pl.Ficha.Genoma != null) pl.Ficha.Genoma.Class = pl.Class;
		pl.Ficha.SaiyanLineage = "Saiyan";
		pl.Ficha.godki ??= new GodKiState();
		pl.Ficha.godki.awakened = true;
		pl.Ficha.godki.mastery = 40;
		pl.Ficha.BP = 1e13;
		pl.Ficha.Statify();
		pl.Ficha.KO = false;
		pl.Ficha.dead = false;

		EstadoDeForma est = pl.Forma;
		est.GradesLigados = false;
		foreach (string id in FormasDaBancadaDasTeclas)
		{
			est.Liberar(id);
			est.EstreiaVista.Add(Catalogo.Rede(id));
		}
		est.Maestria.Por("ssj1", 40);
		est.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct);
		est.Maestria.Por("ssj3", 0);
		est.Maestria.Por("ssg", 60);
		est.Maestria.Por("blue", 60);
		FtABase(pl);
	}

	/// <summary>De volta a base pelo "voltar ao normal" de producao, sem cena pendurada e com o tanque cheio.</summary>
	private void FtABase(ServerPlayer pl)
	{
		for (int i = 0; i < 12 && !pl.Forma.NaBase && pl.Forma.Def != null; i++) Transformar(pl, subir: false);
		PassarACena(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>A FORMA DE PARTIDA de uma medida, pelo `EntrarNaForma` de producao (o que todo gesto chama) -- montagem, e nao o gesto medido.</summary>
	private void FtEm(ServerPlayer pl, string forma)
	{
		FtABase(pl);
		EntrarNaForma(pl, Catalogo.Def(forma)!);
		PassarACena(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>Um gesto pelo fio, e o que o servidor disse por causa dele (o `EscutaDeAvisos`). A cena que ele abriu e passada.</summary>
	private List<string> FtGesto(MesaDeClientesCrus mesa, ServerPlayer pl, Action mandar)
	{
		EscutaDeAvisos = [];
		mandar();
		mesa.Bombeia(() => EscutaDeAvisos is { Count: > 0 }, 3000);
		mesa.Bombeia(() => false, 60);   // o resto do mesmo gesto (o segundo aviso de uma frase dobrada)
		List<string> ditos = EscutaDeAvisos ?? [];
		EscutaDeAvisos = null;
		PassarACena(pl);
		return ditos;
	}

	/// <summary>O C duplo (`subir`) ou o X pelo fio -- o `C2S.Transformar` que o `LocalPlayer.LerTeclaC` manda.</summary>
	private List<string> FtC(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl, bool subir = true)
	{
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return FtGesto(mesa, pl, () =>
		{
			NetDataWriter w = Protocol.Begin(Protocol.C2S.Transformar);
			w.Put(subir);
			cli.Mandar(w);
		});
	}

	/// <summary>Um verb pelo fio -- o `C2S.Verbo` que o `GameClient.SendVerbo` monta (o botao da aba Other e a tecla de forma).</summary>
	private List<string> FtVerbo(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl, string cmd, string arg = "") =>
		FtGesto(mesa, pl, () =>
		{
			NetDataWriter w = Protocol.Begin(Protocol.C2S.Verbo);
			w.Put(cmd);
			w.Put(arg);
			cli.Mandar(w);
		});

	/// <summary>A tecla de uma forma (`forma &lt;id&gt;`), com o Ki na fracao pedida.</summary>
	private List<string> FtTecla(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl, string forma, double ki = 1)
	{
		pl.Ficha.Ki = pl.Ficha.MaxKi * ki;
		return FtVerbo(mesa, cli, pl, "forma", forma);
	}

	private static bool FtDito(List<string> ditos, string termo) => ditos.Any(a => a.Contains(termo, StringComparison.OrdinalIgnoreCase));

	private static string FtJuntos(List<string> ditos) => ditos.Count == 0 ? "(nada dito)" : string.Join(" | ", ditos);

	private static string FtTxt(bool? b) => b?.ToString() ?? "nulo";
}
