using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// ============================ O QUE ERA DE QUEM SAIU DA VAGA -- o dono, 2026-10-19 ============================
/// A assinatura de um personagem e `hash(conta, vaga)` (`ServerPlayer.AssinaturaDe`). No DM ela era SORTEADA na criacao
/// (`CreationUI.dm:199-203`), e a de um personagem apagado nunca mais voltava; aqui ela VOLTA, e o proximo personagem da vaga herdava
/// tudo o que o mundo guardava por ela. A aba People ja esquece (`GameServer.ConvivioReciclado.cs`); este arquivo e o resto, cada coisa
/// como o dono decidiu quando perguntado, pelo MESMO funil -- o `PurgarAssinatura` (apagar pela selecao, reencarnar, ser absorvido ou
/// consumido numa fusao, e CRIAR na vaga, que limpa o que sobrou de antes deste conserto):
///   * OS BEBES A CAMINHO, "como no DM": a GRAVIDEZ morre com a mae (no DM ela morava no corpo dela, `mom.Pregnant`, `Mating.dm:189-197`);
///     o OVO (no DM um mob no chao, `Mating.dm:230`) e o filho do PAI que saiu continuam na fila, orfaos -- nascem com os nomes dos pais
///     antigos, e a vaga nao e mais mae nem pai de ninguem;
///   * OS DOMINIOS voltam pro mundo na hora (a bandeira cai, o planeta fica livre), e os recados guardados pro antigo somem;
///   * O TANQUE DE BIO-ANDROIDE espera pra sempre, como no DM (o parto so acontece com o criador online, `DNALabs.dm:355-367`, e ele nunca
///     volta): o personagem novo da vaga deixa de ser quem a criatura consome;
///   * AS SUPER ESFERAS dele ficam livres -- ou, se ele as guardava por procuracao, voltam pra quem emprestou; se ele era o pedinte, a
///     procuracao cai;
///   * A ESTATUA DO DRAGAO que ele ergueu fica INERTE e sem criador: so OUTRO Namekuseijin do Cla do Dragao a desperta, e vira o criador
///     dela (`ReviverOSet`; o botao e o fato `EstatuaSemCriadorAqui`);
///   * AS ESPERAS NA MEMORIA: a de 30 min entre invasoes (`_proximaCampanha`) e o barrado do Torneio do Cell.
///
/// O QUE FICA, tambem por decisao dele: a AMOSTRA DE DNA (*"o dna deve manter mesmo q o personagem tenha reencarnado"*) e o direito de
/// BATIZAR o mundo que o antigo descobriu. A furia ao achar o corpo (`GameServer.RaivaDoCorpo.cs`) nao guarda nada proprio: ela le a aba
/// People, que ja esqueceu quem saiu.
///
/// Bancada: `--recicladoteste`, as familias M (`GameServer.RecicladoMundoTeste.cs`). Cada decisao tem o seu defeito injetado abaixo.
/// ==========================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): a vaga reciclada nao mexe nos bebes -- a gravidez da apagada passa pra nova da vaga. Falso em jogo.</summary>
	internal static bool BebesHerdamAVagaDeTeste;

	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): os dominios e os recados do antigo passam pro novo da vaga. Falso em jogo.</summary>
	internal static bool DominiosHerdamAVagaDeTeste;

	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): o tanque de bio continua chaveado na vaga -- o novo dela vira o bio no parto. Falso em jogo.</summary>
	internal static bool FornadaHerdaAVagaDeTeste;

	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): as Super Esferas e a procuracao do antigo passam pro novo da vaga. Falso em jogo.</summary>
	internal static bool SuperEsferasHerdamAVagaDeTeste;

	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): a estatua do antigo continua de pe e o novo da vaga e o criador dela. Falso em jogo.</summary>
	internal static bool EstatuaHerdaAVagaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--recicladoteste`, M): a estatua sem criador so acorda pelo criador, que nao existe mais -- o Cla do Dragao nao a
	/// desperta nem ve o botao. Lido pelo <see cref="EstatuaSemCriador"/>. Falso em jogo.
	/// </summary>
	internal static bool EstatuaSemCriadorSoOCriadorDespertaDeTeste;

	/// <summary>DEFEITO INJETADO (`--recicladoteste`, M): as esperas na memoria (invasao, Torneio do Cell) passam pro novo da vaga. Falso em jogo.</summary>
	internal static bool EsperasHerdamAVagaDeTeste;

	/// <summary>
	/// O CRIADOR DE UM TANQUE QUE NAO VOLTA: uma assinatura que nenhuma vaga produz (as de verdade sao 10 digitos). Nao pode ser vazia -- a
	/// fornada sem assinatura vale pela CONTA (`EhOCriadorDaFornada`), e ai outro personagem da mesma conta viraria o bio.
	/// </summary>
	internal const string CriadorQueNaoVolta = "-";

	/// <summary>A VAGA PERDEU (OU GANHOU) UM PERSONAGEM: o que o mundo guardava pela assinatura dele. Chamado do `PurgarAssinatura`.</summary>
	private void EsquecerAAssinaturaNoMundo(string sig)
	{
		if (sig.Length == 0) return;
		EsquecerAAssinaturaNosBebes(sig);
		DevolverOsDominios(sig);
		DeixarOTanqueEsperando(sig);
		LargarAsSuperEsferas(sig);
		AdormecerAEstatua(sig);
		EsquecerAsEsperas(sig);
		EsquecerOAprendiz(sig);   // (2026-10-19) o vinculo de aprendiz de Kaioshin era do personagem que saiu da vaga
		EsquecerAsAmizades(sig);
		VagarOsTronosDaAssinatura(sig);   // (2026-10-26) o CARGO era do personagem que saiu da vaga -- `GameServer.Ranks.cs`   // (2026-10-21) os lacos de amizade com os habitantes (os aliados vao embora com a ficha) -- `GameServer.Amizade.cs`
	}

	/// <summary>
	/// OS BEBES, como no DM: a gravidez da mae que saiu se perde (o pai, se estiver no jogo, fica sabendo); o ovo dela e o filho do pai que
	/// saiu ficam orfaos daquela ponta -- o nome continua na oferta da criacao, a assinatura nao.
	/// </summary>
	private void EsquecerAAssinaturaNosBebes(string sig)
	{
		if (BebesHerdamAVagaDeTeste) return;
		int perdidos = 0, orfaos = 0;
		foreach (BebeACaminho b in _bebes.Bebes.ToList())
		{
			if (!b.DeOvo && b.SigMae == sig)
			{
				_bebes.Bebes.Remove(b);
				perdidos++;
				if (JogadorDaAssinatura(b.SigPai) is { } pai) Avisar(pai, $"a gravidez de {b.Mae} se perdeu: {b.Mae} deixou de existir.");
				continue;
			}
			if (b.SigMae == sig) { b.SigMae = ""; orfaos++; }
			if (b.SigPai == sig) { b.SigPai = ""; orfaos++; }
		}
		if (perdidos + orfaos == 0) return;
		SalvarBebes();
		GD.Print($"[server] filhos: a assinatura {sig} foi reciclada -- {perdidos} gravidez(es) perdida(s), {orfaos} bebe(s) orfao(s)");
	}

	/// <summary>
	/// OS DOMINIOS VOLTAM PRO MUNDO, pelo funil de toda perda (`PerderDominio`: a bandeira cai e o batismo volta a quem tiver a vez), e os
	/// recados guardados pra ele somem -- inclusive o de uma invasao em curso contra o dominio que ele tinha, que seria entregue ao novo.
	/// </summary>
	private void DevolverOsDominios(string sig)
	{
		if (DominiosHerdamAVagaDeTeste) return;
		bool mexeu = _recadosDeConquista.Remove(sig);
		foreach (Dominio d in DominiosDe(sig))
		{
			PerderDominio(d, "", "personagem-apagado", anunciar: true);
			mexeu = true;
		}
		foreach (Invasao inv in _invasoes.Values)
			if (inv.DonoAntigoAssinatura == sig) inv.DonoAntigoAssinatura = "";
		if (mexeu) SalvarConquista();
	}

	/// <summary>
	/// O TANQUE ESPERA PRA SEMPRE: o criador vira <see cref="CriadorQueNaoVolta"/>, e nenhum corpo -- nem o novo da vaga, nem outro da conta --
	/// e quem a criatura consome. O tanque fica gestando ate alguem destruir o laboratorio, como no DM com o criador que nunca loga.
	/// </summary>
	private void DeixarOTanqueEsperando(string sig)
	{
		if (FornadaHerdaAVagaDeTeste) return;
		bool mexeu = false;
		foreach (Obra lab in _noChao)
			if (lab.Fornada is { } g && g.DonoAssinatura == sig) { g.DonoAssinatura = CriadorQueNaoVolta; mexeu = true; }
		if (mexeu) GravarMundo();
	}

	/// <summary>
	/// AS SUPER ESFERAS: se ele era o PEDINTE, a procuracao cai (as sete ficam com quem as guarda); se ele as GUARDAVA por procuracao, elas
	/// voltam pra quem emprestou -- e o `RevogarAGuarda` feito por ele; sem procuracao, as dele ficam livres.
	/// </summary>
	private void LargarAsSuperEsferas(string sig)
	{
		if (SuperEsferasHerdamAVagaDeTeste) return;
		if (HaProcuracao && _benefSig == sig) QuebrarAProcuracao("quem emprestou as esferas deixou de existir");
		if (QuantasSupersTem(sig) == 0) return;

		bool voltam = HaProcuracao;
		string pra = _benefNome;
		foreach (SuperEsfera s in _supers)
		{
			if (s.Dono != sig) continue;
			s.Dono = voltam ? _benefSig : "";
			s.DonoNome = voltam ? _benefNome : "";
		}
		if (voltam) _benefSig = _benefNome = _pedidoDoBenef = _alvoDoPedido = "";
		SalvarSupers();
		foreach (ServerPlayer p in Pessoas) MandarSupers(p);
		AnunciarSupers(voltam ? $"quem guardava as Super Esferas deixou de existir -- elas voltam para {pra}."
							  : "Super Esferas perderam o dono: quem as tinha deixou de existir.");
	}

	/// <summary>
	/// A ESTATUA FICA INERTE E SEM CRIADOR: o set dorme, ninguem a reconfigura, e so outro Namekuseijin do Cla do Dragao a desperta
	/// (`ReviverOSet`). O nome de quem a ergueu fica, como historia ("erguida por X").
	/// </summary>
	private void AdormecerAEstatua(string sig)
	{
		if (EstatuaHerdaAVagaDeTeste) return;
		List<SetDeEsferas> dele = _sets.FindAll(s => !s.Eterno && s.CriadorSig == sig);
		foreach (SetDeEsferas s in dele)
		{
			s.Inerte = true;
			s.CriadorSig = s.CriadorConta = "";
			GD.Print($"[esferas] set {s.Etiqueta} ficou inerte e sem criador: {s.CriadorNome} deixou de existir");
		}
		if (dele.Count == 0) return;
		SalvarEsferas();
		foreach (SetDeEsferas s in dele) MandarEsferas(s.Zona);
	}

	/// <summary>Esta estatua perdeu o criador e esta inerte -- e o Cla do Dragao a desperta. A regra do botao e a do `ReviverOSet`.</summary>
	internal static bool EstatuaSemCriador(SetDeEsferas s) =>
		!EstatuaSemCriadorSoOCriadorDespertaDeTeste && !s.Eterno && s.Inerte && s.CriadorSig.Length == 0;

	/// <summary>A estatua deste mundo perdeu o criador? E o fato `EstatuaSemCriadorAqui`.</summary>
	private bool EstatuaSemCriadorAqui(Jandirus.Core.World.ZoneKey zona) =>
		_sets.Exists(s => s.Zona.Hash == zona.Hash && EstatuaSemCriador(s));

	/// <summary>O portao de raca e classe do `ErguerEstatua`: so o Cla do Dragao carrega o segredo das esferas.</summary>
	private static bool DoClaDoDragao(ServerPlayer pl) =>
		string.Equals(pl.Race, "Namekian", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(pl.Class, "Dragon clan", StringComparison.OrdinalIgnoreCase);

	/// <summary>Quem desperta a estatua sem criador vira o criador dela: dali em diante e dele, com o bloqueio e a morte que inerta.</summary>
	private void AdotarAEstatua(SetDeEsferas s, ServerPlayer pl)
	{
		string antes = s.CriadorNome;
		s.CriadorSig = pl.Assinatura;
		s.CriadorConta = pl.Conta;
		s.CriadorNome = pl.Name;
		GD.Print($"[esferas] {pl.Name} despertou o set {s.Etiqueta}, que era de {antes}, e virou o criador dele");
	}

	/// <summary>AS ESPERAS NA MEMORIA: a proxima campanha de invasao e o "ja lutou"/"se meteu" do Torneio do Cell de pe.</summary>
	private void EsquecerAsEsperas(string sig)
	{
		if (EsperasHerdamAVagaDeTeste) return;
		_proximaCampanha.Remove(sig);
		if (_torneioDoCell is { } t) { t.JaLutaram.Remove(sig); t.Barrados.Remove(sig); }
	}
}
