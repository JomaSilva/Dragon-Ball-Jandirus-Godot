using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T7 da tech) A BANCADA DE CRIACAO -- a vitrine de roupas, o pente e a tinta de olhos. A regra mora em
/// `Core/Tech/BancadaDeCriacao.cs`, com o que o original faz e o que muda aqui.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os verbos da bancada de criacao (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeBancadaDeCriacao(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case BancadaDeCriacao.VerboRoupa: FazerARoupa(pl, arg); return true;
			case BancadaDeCriacao.VerboItem: FazerOItemDaBancada(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>As acoes de mochila do que a bancada faz. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDaBancadaDeCriacao(ServerPlayer pl, string acao, string arg2)
	{
		switch (acao)
		{
			case BancadaDeCriacao.AcaoPentear: Pentear(pl, arg2); return true;
			case BancadaDeCriacao.AcaoTingirOsOlhos: TingirOsOlhos(pl, arg2); return true;
			default: return false;
		}
	}

	/// <summary>
	/// A bancada ao alcance, pronta pra trabalhar -- ou a recusa dita, e nulo. *"You need to bolt [name] before you can
	/// use it!"*
	/// </summary>
	private Obra? BancadaPronta(ServerPlayer pl, string verbo)
	{
		Obra? o = ObraQueAceita(pl, verbo);
		if (o == null) { Avisar(pl, "não há bancada de criação por perto."); return null; }
		if (!o.Aparafusada && !BancadaDeCriacao.TrabalhaSoltaDeTeste)
		{ Avisar(pl, $"aparafuse {NomeDaObra(o)} antes de usá-la."); return null; }
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não faz nada na bancada."); return null; }
		return o;
	}

	/// <summary>Guarda o que a bancada fez e cobra por ele -- ou recusa sem cobrar. Verdadeiro se entregou.</summary>
	private bool EntregarDaBancada(ServerPlayer pl, string item, double preco, string oQue)
	{
		if (pl.Mochila.Guardar(item) > 0) { Avisar(pl, "sua mochila está cheia."); return false; }
		if (preco > 0 && !CobrarOuExplicar(pl, preco, Moeda.Zeni, oQue))
		{
			pl.Mochila.Tirar(item);
			return false;
		}
		MandarMochila(pl);
		return true;
	}

	/// <summary>
	/// A VITRINE (`Clothes` -> `ClothingChoice()` -> `Clothes(clthcost, objtype)`): a peca escolhida, na cor escolhida,
	/// pelo preco dela, vai pra mochila. O pedido e `<folha>` ou `<folha>/<RRGGBB>`.
	/// </summary>
	private void FazerARoupa(ServerPlayer pl, string pedido)
	{
		if (BancadaPronta(pl, BancadaDeCriacao.VerboRoupa) == null) return;
		string[] p = pedido.Split('/');
		string folha = p[0].Trim();
		if (folha.Length == 0) { Avisar(pl, "escolha a peça na tela que a bancada abre."); return; }

		int? daVitrine = BancadaDeCriacao.PrecoDaRoupa(folha);
		if (daVitrine == null && !BancadaDeCriacao.FazQualquerPecaDeGracaDeTeste) { Avisar(pl, "a bancada não sabe fazer essa peça."); return; }
		RoupaGuardada? peca = RoupaGuardada.Ler($"{RoupaGuardada.Prefixo}{folha}|{(p.Length > 1 ? p[1].Trim() : "")}");
		if (peca == null) { Avisar(pl, "essa cor não existe: são seis dígitos (RRGGBB)."); return; }
		if (peca.Ficha(CatalogoDeItens.Visual) is not { } ficha) { Avisar(pl, "a bancada não sabe fazer essa peça."); return; }

		double preco = BancadaDeCriacao.FazQualquerPecaDeGracaDeTeste ? 0 : daVitrine ?? 0;
		if (!EntregarDaBancada(pl, peca.Id, preco, ficha.Nome)) return;
		Falar(pl, Protocol.Fala.Emote, $"costura uma peça de roupa na bancada.");
		Avisar(pl, $"{ficha.Nome} pronta, por {preco:N0} zeni: está na mochila -- é só vestir.");
		GD.Print($"[server] {pl.Name} fez a roupa {peca.Id} na bancada ({preco:0} zeni)");
	}

	/// <summary>"Items": o pente e a tinta de olhos, cem zeni cada.</summary>
	private void FazerOItemDaBancada(ServerPlayer pl, string qual)
	{
		if (BancadaPronta(pl, BancadaDeCriacao.VerboItem) == null) return;
		if (qual is not (BancadaDeCriacao.Pente or BancadaDeCriacao.TintaDeOlhos) || CatalogoDeItens.Get(qual) is not { } def)
		{ Avisar(pl, "a bancada não faz isso."); return; }
		if (!EntregarDaBancada(pl, def.Id, BancadaDeCriacao.CustoDoItem, def.Nome)) return;
		Avisar(pl, $"{def.Nome} pronto, por {BancadaDeCriacao.CustoDoItem:N0} zeni: está na mochila.");
	}

	// =====================================================================
	// O PENTE E A TINTA DE OLHOS -- `Change_Hair()` e `Change_Eye_Color()`
	// =====================================================================
	/// <summary>
	/// O `Change_Hair()` (`usr.Hair(1)`, `HairObject.dm:117-142`): a cor e o penteado. O pedido e `<penteado>` ou
	/// `<penteado>/<RRGGBB>`; sem cor, fica a que o cabelo ja tinha.
	/// </summary>
	private void Pentear(ServerPlayer pl, string pedido)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não se penteia."); return; }
		if (!VisualCatalog.TemCabelo(pl.Race)) { Avisar(pl, "este corpo não tem cabelo pra pentear."); return; }
		string[] p = pedido.Split('/');
		string penteado = p[0].Trim();
		if (penteado.Length == 0) { Avisar(pl, "escolha o penteado na tela que o pente abre."); return; }
		if (CatalogoDeItens.Visual is not { } cat || !cat.TemCabeloChamado(penteado)) { Avisar(pl, "esse penteado não existe."); return; }

		Rgb? cor = pl.Visual.CorCabelo;
		if (p.Length > 1 && p[1].Trim().Length > 0)
		{
			if (!BancadaDeCriacao.LerCor(p[1].Trim(), out Rgb lida)) { Avisar(pl, "essa cor não existe: são seis dígitos (RRGGBB)."); return; }
			cor = lida;
		}
		// *"Saiyan hair created from here will always be black."* -- a regra da criacao (`VisualCatalog.Sanear`).
		if (VisualCatalog.CabeloNatural(pl.Race) && !BancadaDeCriacao.SaiyajinTingeDeTeste) cor = null;

		pl.Visual.Cabelo = penteado;
		pl.Visual.CorCabelo = cor;
		ReapresentarAparencia(pl);
		Persistir(pl);
		Falar(pl, Protocol.Fala.Emote, "passa o pente e muda o penteado.");
		Avisar(pl, VisualCatalog.CabeloNatural(pl.Race) && p.Length > 1 && p[1].Trim().Length > 0
			? $"penteado novo: {penteado}. (O cabelo de um Saiyajin não pega tinta.)"
			: $"penteado novo: {penteado}.");
	}

	/// <summary>O `Change_Eye_Color()` (`usr.Eyes()`): a cor dos olhos. O pedido e `<RRGGBB>`.</summary>
	private void TingirOsOlhos(ServerPlayer pl, string pedido)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		if (!VisualCatalog.CorpoTemRosto(pl.Visual, pl.Race)) { Avisar(pl, "este corpo não tem olhos pra tingir."); return; }
		if (pedido.Trim().Length == 0) { Avisar(pl, "escolha a cor na tela que a tinta abre."); return; }
		if (!BancadaDeCriacao.LerCor(pedido.Trim(), out Rgb cor)) { Avisar(pl, "essa cor não existe: são seis dígitos (RRGGBB)."); return; }

		pl.Visual.CorOlho = cor;
		ReapresentarAparencia(pl);
		Persistir(pl);
		Falar(pl, Protocol.Fala.Emote, "pinga a tinta nos olhos, e a cor deles muda.");
		Avisar(pl, "a cor dos seus olhos mudou.");
	}
}
