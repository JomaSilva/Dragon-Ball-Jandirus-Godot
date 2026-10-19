using Godot;

namespace Jandirus.Server;

/// <summary>
/// ============================ O BEBE (2026-10-18) ============================
/// O dono: *"faça com q Saiyajins, meio saiyajins, e humanos filhos de players tenham sempre esse sprite por 1 hora no jogo, mostrando q
/// eles acabaram de nascer, dps dessa 1 hora o jogador vira o sprite normal q ele fez na criação com as roupas escolhidas etc, na forma
/// de bebe eles so vao ser o sprite sem overlay"* -- e logo depois: *"Baby boy se o personagem for homem e baby girls se for mulher"*.
///
///   * QUEM NASCE BEBE: o parto de um bebe a caminho (`CreateChar`), quando o filho e Humano, Saiyajin ou meio-Saiyajin
///     (`Filhos.NasceBebe`). Ninguem que ja existia vira bebe.
///   * O RELOGIO SO ANDA COM ELE NO MUNDO (<see cref="TickDosBebes"/>, a 1 Hz), e o que falta vai no save
///     (`CharacterSave.BebeRestanteS`): uma hora de JOGO. Deslogar nao faz crescer.
///   * QUEM DESENHA E O CLIENTE: o pacote de aparencia leva o bit (`PacoteDeAparencia`, o segundo byte de marcas), o corpo vira a folha
///     do bebe (`VisualCatalog.CorpoSprite`) e o boneco nao veste nada por cima (`CharacterVisual.Escondida`).
///   * CRESCER E REAPRESENTAR A APARENCIA a zona (`ReapresentarAparencia`): a roupa, o cabelo e o resto voltam no mesmo pacote, e o
///     dono do corpo le no chat que cresceu.
///
/// SO MUDA O DESENHO: o bebe luta, treina e voa como qualquer um -- o pedido e o sprite.
/// =============================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--filhosteste`): o filho nasce grande, como antes do pedido. Falso em jogo, sempre.</summary>
	internal static bool FilhoNasceGrandeDeTeste;

	/// <summary>DEFEITO INJETADO (`--filhosteste`): o relogio do bebe parado -- ele nunca cresce. Falso em jogo, sempre.</summary>
	internal static bool BebeNaoCresceDeTeste;

	/// <summary>DEFEITO INJETADO (`--filhosteste`): o pacote de aparencia sem a marca do bebe. Falso em jogo, sempre.</summary>
	internal static bool BebeSemMarcaNoPacoteDeTeste;

	/// <summary>Este corpo esta na hora de bebe? E a pergunta do pacote de aparencia.</summary>
	private static bool EhBebe(ServerPlayer p) => p.BebeRestanteS > 0;

	/// <summary>
	/// O RELOGIO DOS BEBES, a 1 Hz: desconta o tempo de quem esta no mundo e faz crescer quem chegou ao fim. So JOGADOR conta -- o corpo
	/// sem dono (o clone, o boneco do corpo largado) nao e o bebe de ninguem, e o deslogado nem esta aqui.
	/// </summary>
	private void TickDosBebes(double dt)
	{
		if (BebeNaoCresceDeTeste) return;
		foreach (ServerPlayer pl in Jogadores)
		{
			if (!EhBebe(pl)) continue;
			pl.BebeRestanteS = Math.Max(0, pl.BebeRestanteS - dt);
			if (!EhBebe(pl)) Crescer(pl);
		}
	}

	/// <summary>A HORA ACABOU: o corpo volta a ser o da criacao pra zona inteira, e o dono dele fica sabendo.</summary>
	private void Crescer(ServerPlayer pl)
	{
		ReapresentarAparencia(pl);
		Avisar(pl, "Você cresceu! Agora você tem a aparência que escolheu na criação.");
		GD.Print($"[server] {pl.Name} cresceu: a hora de bebe acabou");
	}

	/// <summary>
	/// NO LOGIN, O BEBE SABE QUANTO FALTA -- sem isto o jogador entraria na folha do bebe sem saber por que, nem ate quando. Em minutos de
	/// jogo, arredondados pra cima: "falta 1 minuto" ate o ultimo segundo.
	/// </summary>
	private void AvisarDoBebeNoLogin(ServerPlayer pl)
	{
		if (!EhBebe(pl)) return;
		int minutos = (int)Math.Ceiling(pl.BebeRestanteS / 60);
		Avisar(pl, minutos == 1
			? "Você é um bebê: falta 1 minuto de jogo pra você crescer e ganhar a aparência que escolheu na criação."
			: $"Você é um bebê: faltam {minutos} minutos de jogo pra você crescer e ganhar a aparência que escolheu na criação.");
	}
}
