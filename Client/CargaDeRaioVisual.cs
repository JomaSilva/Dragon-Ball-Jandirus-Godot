using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using static Jandirus.Client.ArteDeKiNoCliente;

namespace Jandirus.Client;

/// <summary>
/// A CARGA DO RAIO -- o brilho que nasce na mao de quem esta reunindo energia pra um feixe.
///
/// ============================ O QUE ELE E NO ORIGINAL ============================
/// `addchargeoverlay()` (`Skills/Ki/tools/mobhandler.dm:18-32`), chamado pelo bloco de carga de todo
/// verb de raio (`beams.dm:300` e as irmas). Sao cinco linhas e cada uma virou uma decisao aqui:
///
///   `I.icon = 'BlastCharges.dmi'`            -> desenhado por shader, ver abaixo
///   `I.icon_state = ChargeState`             -> <see cref="Estado"/>, de 1 a 9, ver `EntityState.CargaDoCanal`
///   `I.icon += rgb(blastR,blastG,blastB)`    -> <see cref="Cor"/>, somada aos tons da folha
///   `I.layer = MOB_LAYER+99` / `I.plane = 7` -> <see cref="ZIndex"/> 1, por cima do boneco
///   `while(charging) sleep(1)`               -> quem cria e destroi e o `World`, pelo estado do fio
/// ==============================================================================
///
/// ============================ A FOLHA VIROU SHADER, JUNTO COM O TIRO ============================
/// Ele era um `AnimatedSprite2D` da `BlastCharges.dmi`. Quando o dono mandou trocar os ataques de ki
/// por shader (2026-10-07), a carga foi junto pelo motivo que este arquivo ja escrevia sobre a luz
/// dela: *o brilho que se junta na mao E um ataque de ki* -- e a arte do raio, na cor de ki do mesmo
/// dono, e morre no instante em que o feixe nasce. Uma carga de folha pixelada virando um raio de
/// shader seria a emenda mais visivel do jogo, e ela acontece em TODO disparo.
///
/// Os NOVE desenhos continuam nove (o `ChargeState` e por personagem e nao muda nunca): cada um e um
/// estilo de bola em `ArteDeKiNoCliente.EstiloDaCarga`, lido da folha de mesmo numero.
///
/// O QUE A FOLHA NAO FAZIA E ESTE FAZ: a bola ENCHE. Ela nasce pequena e chega ao tamanho em meio
/// segundo -- energia que "se junta" e nao um adesivo que aparece pronto.
/// ==============================================================================================
///
/// ============================ ELE NAO E A CHAMA DO `C`, E A DIFERENCA IMPORTA ============================
/// A <see cref="CargaVisual"/> ali do lado desenha a aura de POWER-UP -- a do `Power_Up` do DM, a que
/// o jogador acende segurando C, a que ilumina e zumbe. Esta aqui e outra coisa inteira: e o brilho
/// que se junta na mao ANTES de um raio sair, ele e por personagem (nove desenhos diferentes,
/// sorteados no nascimento) e ele morre no instante em que o feixe nasce.
///
/// Os nomes sao parecidos porque o DM tambem os tem parecidos (`chargeaura` x `BlastCharges`), e por
/// isso vale dizer com todas as letras: **carregar Ki e carregar um raio sao dois estados
/// independentes**, e um corpo pode estar nos dois ao mesmo tempo. Se um dia so um dos dois aparecer
/// na tela, o defeito esta em quem os LIGA (o `World`), e nao aqui.
/// ======================================================================================================
///
/// ============================ E O CORPO POR BAIXO FICA NO IDLE ============================
/// Nao ha pose de carregar, e isso nao e falta de arte -- e o original. O bloco de carga do verb
/// escreve `forceicon`, som, `canmove = 0`, `charging = 1` e este overlay, e **nao toca em
/// `icon_state`** (`beams.dm:288-300`); o corpo so muda de pose no `beaming` (`:280`). Ha ate a prova
/// de que alguem tentou o contrario e desistiu: no Boom Wave a linha esta la, comentada --
/// `//usr.icon_state="Blast"` (`beams.dm:485`).
///
/// Por isso este node desenha por CIMA de um boneco parado, em vez de trocar a folha dele.
/// =====================================================================================
///
/// QUEM O CRIA E O DESTROI e o <see cref="World.MarcarCargaDeRaio"/>, pelo `EntityState.Canal` do
/// snapshot. Ele nao guarda estado nenhum -- existir JA E o estado, o mesmo desenho da
/// <see cref="NaveDesenhada"/> (e pelo mesmo motivo: filho morre com o pai, entao o corpo que sai de
/// vista leva o brilho junto sem ninguem lembrar de apagar nada).
/// </summary>
public partial class CargaDeRaioVisual : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem cria e quem destroi procuram por ele.</summary>
	public const string NomeDoNode = "CargaDeRaio";

	/// <summary>
	/// A que distancia do centro do corpo a bola se junta, em px, pro lado que ele olha. E onde ficam
	/// as maos de um boneco de 32 px com os bracos a frente -- a folha do original desenhava o brilho
	/// deslocado do centro em cada direcao pelo mesmo motivo (ver <see cref="Direcao"/>).
	/// </summary>
	private const float DistanciaDaMao = 9f;

	/// <summary>Em quanto tempo a bola enche, e de que fracao do tamanho ela parte.</summary>
	private const float SegundosPraEncher = 0.5f, TamanhoDeNascenca = 0.4f;

	/// <summary>
	/// QUAL DOS NOVE DESENHOS -- o `ChargeState` do personagem. De 1 a 9.
	///
	/// Vem do fio (`EntityState.CargaDoCanal`) e nao e sorteado aqui: o desenho tem que ser o MESMO
	/// na tela de todo mundo, e um sorteio local daria a cada espectador uma carga diferente pro
	/// mesmo corpo. Quem o deriva e o servidor, por funcao pura da identidade -- ver
	/// `ArteDeProjetil.CargaDeRaio`.
	/// </summary>
	public int Estado = 1;

	/// <summary>
	/// `I.icon += rgb(blastR,blastG,blastB)` (`mobhandler.dm:8`) -- a cor de Ki do DONO, e nao a da
	/// chama. Ver `World.CorDoKiDe`, que e quem a resolve e por que as duas nao sao a mesma pergunta.
	/// </summary>
	public Color Cor = Aura.CorDoKiCru;

	/// <summary>
	/// PRA ONDE O CORPO OLHA. No BYOND o overlay e filho do mob e HERDA o `dir` dele -- o brilho nasce
	/// na MAO, e a mao muda de lado. Aqui e a mesma coisa em uma conta: a bola fica
	/// <see cref="DistanciaDaMao"/> a frente do corpo, na direcao dele.
	/// </summary>
	public Facing Direcao = Facing.South;

	private ShaderMaterial? _mat;
	private EstiloDeBola _estilo = EstiloDaCarga(1);
	private double _idade;

	public override void _Ready()
	{
		// POR CIMA DO BONECO -- `layer = MOB_LAYER+99`, `plane = 7` (`mobhandler.dm:3-4`). O brilho
		// que se junta na mao fica na FRENTE do corpo; desenhado por tras ele viraria um halo atras
		// dos ombros, que e outra coisa.
		ZIndex = 1;

		Vestir();

		// ============================ A CARGA TAMBEM LANCA LUZ, E AQUI ESTA O PORQUE ============================
		// O pedido do dono foi *"beams e ataque de ki deveriam ter LUZ PROPRIA"*, e o brilho que se
		// junta na mao E um ataque de ki -- e literalmente a arte do raio, tingida com a cor de ki do
		// mesmo dono, e ele morre no instante em que o feixe nasce. Um brilho de energia que nao acende
		// nada em volta le como adesivo colado no boneco.
		//
		// ============================ E ELA NAO E A CHAMA DO `C`, QUE CONTINUA SEM LUZ ============================
		// A <see cref="CargaVisual"/> ali do lado -- a aura de power-up que o jogador acende segurando
		// C -- continua sem `PointLight2D` nenhuma, por decisao escrita do dono (ver `Aura.Aplicar`:
		// dar luz a ela seria um TERCEIRO dono do mesmo efeito, e faria a BASE voltar a brilhar, que e
		// a queixa que aquele conserto existe pra matar). Carregar KI e carregar UM RAIO sao dois
		// estados independentes, e este arquivo ja dizia isso no cabecalho. So o segundo acende.
		//
		// CUSTA POUCO por construcao: e no maximo UMA luz por corpo canalizando, e canalizar e um
		// estado raro e curto -- contra as ate 256 de um ceu cheio de tiro, que e o que obrigou o
		// teto do `LuzDeKi`. As duas dividem o mesmo orcamento, e e de proposito: o que estoura um
		// quadro e a soma, e nao a origem de cada uma.
		// ====================================================================================================
		LuzDeKi.Pendurar(this, Cor, 1f);
	}

	/// <summary>
	/// O CORPO VIROU (ou o estado mudou). Chamado pelo `World` a cada snapshot enquanto a carga vive.
	///
	/// SO TRABALHA NA MUDANCA: trocar de estado refaz o material (outro estilo), e virar so muda de
	/// que lado a bola e desenhada. Nenhum dos dois zera a idade -- a bola nao volta a encher porque o
	/// corpo olhou pro outro lado.
	/// </summary>
	public void Definir(int estado, Facing dir)
	{
		if (estado == Estado && dir == Direcao) return;
		bool outroDesenho = estado != Estado;
		Estado = estado;
		Direcao = dir;
		if (outroDesenho) Vestir();
		QueueRedraw();
	}

	private void Vestir()
	{
		_estilo = EstiloDaCarga(Estado is >= 1 and <= 9 ? Estado : 1);
		_mat = PintorDeKi.MaterialDeBola(_estilo, _estilo.Raio, Cor);
		Material = _mat;
	}

	public override void _Process(double delta)
	{
		_idade += delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("tempo", (float)_idade);

		// ENCHE: de `TamanhoDeNascenca` ate 1 em `SegundosPraEncher`, desacelerando no fim. O tamanho
		// entra como escala do DESENHO (a bola inteira, halo e pontos em volta crescem juntos) e nao
		// como raio do shader -- trocar o raio a cada quadro faria o ruido de dentro escorregar.
		float t = Mathf.Clamp((float)_idade / SegundosPraEncher, 0f, 1f);
		float tamanho = Mathf.Lerp(TamanhoDeNascenca, 1f, 1f - (1f - t) * (1f - t));

		// O MESMO 4 PRA BAIXO DA NAVE, e pelo mesmo motivo: o centro do node do corpo fica
		// `MoveRules.FeetOffsetY` acima dos pes, e sem o ajuste o brilho sai na altura da testa.
		Vec2 frente = MeleeArea.Frente(Direcao);
		var mao = new Vector2(frente.X, frente.Y) * DistanciaDaMao + new Vector2(0, 4);

		DrawSetTransform(mao, 0f, Vector2.One * tamanho);
		PintorDeKi.Quadro(this, Vector2.Zero, Vector2.Right, PintorDeKi.MeiaDaBola(_estilo, _estilo.Raio));
	}
}
